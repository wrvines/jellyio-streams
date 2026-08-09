using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.AIOStreams.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Result of a sync or add operation.
/// </summary>
public sealed class SyncResult
{
    public DateTime StartedAt { get; set; }

    public DateTime CompletedAt { get; set; }

    public int Movies { get; set; }

    public int Shows { get; set; }

    public int Episodes { get; set; }

    /// <summary>
    /// Gets or sets the total number of .strm files written (one per stream).
    /// </summary>
    public int Streams { get; set; }

    /// <summary>
    /// Gets or sets the total number of files written (.strm + nfo).
    /// </summary>
    public int Files { get; set; }

    /// <summary>
    /// Gets or sets the number of titles that had no playable stream and were skipped.
    /// </summary>
    public int Skipped { get; set; }

    public bool LibraryScanTriggered { get; set; }

    public string? Fingerprint { get; set; }
}

/// <summary>
/// Current sync status of the plugin.
/// </summary>
public sealed class SyncStatus
{
    public bool IsSyncing { get; set; }

    public DateTime? LastStartedAt { get; set; }

    public DateTime? LastCompletedAt { get; set; }

    public string? LastError { get; set; }

    public SyncResult? LastResult { get; set; }

    /// <summary>
    /// Gets or sets the running plugin version (diagnostics).
    /// </summary>
    public string? PluginVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server has an addon URL configured.
    /// </summary>
    public bool AddonUrlConfigured { get; set; }

    /// <summary>
    /// Gets or sets a human readable description of the current sync step (set while syncing).
    /// </summary>
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// Gets or sets the overall sync progress in percent (0-100, set while syncing).
    /// </summary>
    public double PercentComplete { get; set; }
}

/// <summary>
/// Request to add a single title to the library without wiping existing content.
/// </summary>
public sealed class SingleTitleRequest
{
    public string Type { get; set; } = "movie";

    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ReleaseInfo { get; set; }

    public int? MaxStreams { get; set; }
}

/// <summary>
/// Orchestrates pulling catalogs, meta and streams from AIOStreams and writing them
/// as .strm files into the Jellyfin library folder.
/// </summary>
public sealed class CatalogSynchronizer
{
    private readonly AIOStreamsClient _client;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CatalogSynchronizer> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SyncStatus _status = new();

    public CatalogSynchronizer(
        AIOStreamsClient client,
        ILibraryManager libraryManager,
        ILogger<CatalogSynchronizer> logger)
    {
        _client = client;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public SyncStatus GetStatus() => _status;

    /// <summary>
    /// Full sync: wipes the managed folders and rebuilds them from the configured catalogs.
    /// </summary>
    public async Task<SyncResult> SyncCatalogsAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var root = Plugin.Instance!.ResolvedOutputPath;

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BeginSync();

            var manifest = await _client.GetManifestAsync(config.AddonUrl, config.ExtraQuery, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to fetch the AIOStreams manifest. Check the addon URL.");

            var catalogs = SelectCatalogs(manifest, config);
            if (catalogs.Count == 0)
            {
                throw new InvalidOperationException("No movie/series catalogs found in the AIOStreams manifest. Configure the addon first and enable at least one catalog.");
            }

            Directory.CreateDirectory(root);
            StrmLibrary.Wipe(root);

            var result = new SyncResult { StartedAt = DateTime.UtcNow };
            var done = 0;
            var total = catalogs.Count;

            foreach (var catalog in catalogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Syncing catalog {Type}/{Id}", catalog.Type, catalog.Id);
                _status.ProgressMessage = $"Catalog {done + 1}/{total}: {catalog.Type}/{catalog.Id}";
                _status.PercentComplete = total > 0 ? done * 100.0 / total : 0;
                await SyncCatalogAsync(config, catalog, root, result, cancellationToken).ConfigureAwait(false);
                done++;
                progress?.Report(done * 100.0 / total);
                _status.ProgressMessage = $"Catalog {done}/{total} done ({result.Movies} movies, {result.Shows} shows, {result.Episodes} episodes)";
                _status.PercentComplete = total > 0 ? done * 100.0 / total : 0;
            }

            FinishSync(result);
            return result;
        }
        catch (Exception ex)
        {
            FailSync(ex);
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// Adds a single title (movie or series) without wiping existing content.
    /// </summary>
    public async Task<SyncResult> AddTitleAsync(SingleTitleRequest request, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var root = Plugin.Instance!.ResolvedOutputPath;

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new ArgumentException("A title id is required.");
        }

        var type = request.Type.Equals("series", StringComparison.OrdinalIgnoreCase) ? "series" : "movie";

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BeginSync();

            Directory.CreateDirectory(root);
            var result = new SyncResult { StartedAt = DateTime.UtcNow };

            var meta = await _client.GetMetaAsync(config.AddonUrl, config.ExtraQuery, type, request.Id, cancellationToken).ConfigureAwait(false);

            var title = !string.IsNullOrWhiteSpace(request.Name)
                ? request.Name
                : meta?.Meta?.Name ?? request.Id;
            var releaseInfo = request.ReleaseInfo ?? meta?.Meta?.ReleaseInfo;

            if (type == "series")
            {
                var videos = meta?.Meta?.Videos ?? [];
                var episodes = new List<EpisodeGroup>();

                foreach (var video in videos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(video.Id))
                    {
                        continue;
                    }

                    var streams = await ResolveStreamsAsync(config, "series", video.Id, request.MaxStreams, cancellationToken).ConfigureAwait(false);
                    if (streams.Count == 0)
                    {
                        result.Skipped++;
                        continue;
                    }

                    episodes.Add(new EpisodeGroup(
                        video.Season,
                        video.Episode,
                        video.Title ?? video.Name ?? string.Empty,
                        streams));
                }

                if (episodes.Count > 0)
                {
                    var files = StrmLibrary.WriteShow(root, title, StrmLibrary.ExtractYear(releaseInfo), StrmLibrary.ExtractImdbId(request.Id), episodes, cancellationToken);
                    result.Shows++;
                    result.Episodes += episodes.Count;
                    result.Streams += files.Count(f => f.EndsWith(".strm", StringComparison.OrdinalIgnoreCase));
                    result.Files += files.Count;
                }
            }
            else
            {
                var streams = await ResolveStreamsAsync(config, "movie", request.Id, request.MaxStreams, cancellationToken).ConfigureAwait(false);
                if (streams.Count == 0)
                {
                    throw new InvalidOperationException("No playable streams were found for this title.");
                }

                var files = StrmLibrary.WriteMovie(root, title, StrmLibrary.ExtractYear(releaseInfo), StrmLibrary.ExtractImdbId(request.Id), streams, cancellationToken);
                result.Movies++;
                result.Streams += streams.Count;
                result.Files += files.Count;
            }

            FinishSync(result);
            return result;
        }
        catch (Exception ex)
        {
            FailSync(ex);
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task SyncCatalogAsync(
        PluginConfiguration config,
        ManifestCatalog catalog,
        string root,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        var limit = config.MaxItemsPerCatalog > 0 ? config.MaxItemsPerCatalog : 50;

        var page = await _client.GetCatalogAsync(
                config.AddonUrl,
                config.ExtraQuery,
                catalog.Type!,
                catalog.Id!,
                0,
                limit,
                search: null,
                cancellationToken)
            .ConfigureAwait(false);

        var metas = (page?.Metas ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                && !string.IsNullOrWhiteSpace(m.Name)
                && !string.Equals(m.Type, "error", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in metas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _status.ProgressMessage = $"Catalog {catalog.Type}/{catalog.Id}: \"{item.Name}\" — resolving streams…";

            if (string.Equals(item.Type, "series", StringComparison.OrdinalIgnoreCase))
            {
                await SyncSeriesAsync(config, item, root, result, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SyncMovieAsync(config, item, root, result, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SyncMovieAsync(
        PluginConfiguration config,
        MetaPreview item,
        string root,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        var streams = await ResolveStreamsAsync(config, "movie", item.Id!, null, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
        {
            result.Skipped++;
            return;
        }

        var files = StrmLibrary.WriteMovie(
            root,
            item.Name!,
            StrmLibrary.ExtractYear(item.ReleaseInfo),
            StrmLibrary.ExtractImdbId(item.Id),
            streams,
            cancellationToken);

        result.Movies++;
        result.Streams += streams.Count;
        result.Files += files.Count;
    }

    private async Task SyncSeriesAsync(
        PluginConfiguration config,
        MetaPreview item,
        string root,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        if (!config.SyncEpisodes)
        {
            result.Skipped++;
            return;
        }

        var metaResponse = await _client.GetMetaAsync(config.AddonUrl, config.ExtraQuery, "series", item.Id!, cancellationToken).ConfigureAwait(false);
        var meta = metaResponse?.Meta;
        var videos = meta?.Videos ?? [];

        var selected = SelectEpisodes(videos, config.MaxEpisodesPerSeries);
        var groups = new List<EpisodeGroup>();

        foreach (var video in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(video.Id))
            {
                continue;
            }

            var streams = await ResolveStreamsAsync(config, "series", video.Id!, null, cancellationToken).ConfigureAwait(false);
            if (streams.Count == 0)
            {
                result.Skipped++;
                continue;
            }

            groups.Add(new EpisodeGroup(
                video.Season,
                video.Episode,
                video.Title ?? video.Name ?? string.Empty,
                streams));
        }

        if (groups.Count == 0)
        {
            result.Skipped++;
            return;
        }

        var files = StrmLibrary.WriteShow(
            root,
            meta?.Name ?? item.Name!,
            StrmLibrary.ExtractYear(meta?.ReleaseInfo ?? item.ReleaseInfo),
            StrmLibrary.ExtractImdbId(item.Id),
            groups,
            cancellationToken);

        result.Shows++;
        result.Episodes += groups.Count;
        result.Streams += files.Count(f => f.EndsWith(".strm", StringComparison.OrdinalIgnoreCase));
        result.Files += files.Count;
    }

    private static List<MetaVideo> SelectEpisodes(IReadOnlyList<MetaVideo> videos, int maxEpisodes)
    {
        var withIds = videos.Where(v => !string.IsNullOrWhiteSpace(v.Id)).ToList();

        if (maxEpisodes <= 0)
        {
            return withIds;
        }

        return withIds
            .OrderByDescending(v => v.Season ?? int.MaxValue)
            .ThenByDescending(v => v.Episode ?? int.MaxValue)
            .Take(maxEpisodes)
            .ToList();
    }

    private async Task<List<StreamFile>> ResolveStreamsAsync(
        PluginConfiguration config,
        string type,
        string id,
        int? maxStreamsOverride,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetStreamsAsync(config.AddonUrl, config.ExtraQuery, type, id, cancellationToken).ConfigureAwait(false);

        var withUrl = (response?.Streams ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var max = maxStreamsOverride ?? (config.MaxStreamsPerTitle > 0 ? config.MaxStreamsPerTitle : int.MaxValue);
        max = Math.Max(1, max);

        var result = new List<StreamFile>();
        for (var i = 0; i < withUrl.Count && result.Count < max; i++)
        {
            result.Add(new StreamFile(withUrl[i].Url!, BuildLabel(withUrl[i], i)));
        }

        return result;
    }

    private static string BuildLabel(StreamResult stream, int index)
    {
        var label = stream.Title ?? stream.Name;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = $"Stream {index + 1}";
        }

        return StrmLibrary.SanitizeLabel(label);
    }

    private static List<ManifestCatalog> SelectCatalogs(AddonManifest manifest, PluginConfiguration config)
    {
        var enabled = config.EnabledCatalogIds?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => id.ToLowerInvariant())
            .ToHashSet() ?? [];

        return (manifest.Catalogs ?? [])
            .Where(c => string.Equals(c.Type, "movie", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Type, "series", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .Where(c => !c.Id!.Contains("search", StringComparison.OrdinalIgnoreCase))
            .Where(c => enabled.Count == 0 || enabled.Contains(c.Id!.ToLowerInvariant()))
            .DistinctBy(c => $"{c.Type}:{c.Id}")
            .ToList();
    }

    private static PluginConfiguration RequireConfig()
    {
        var plugin = Plugin.Instance
            ?? throw new InvalidOperationException("Plugin is not loaded.");
        var config = plugin.Configuration;

        if (string.IsNullOrWhiteSpace(config.AddonUrl))
        {
            throw new InvalidOperationException("The AIOStreams addon URL is not configured. Open the plugin settings first.");
        }

        return config;
    }

    private void BeginSync()
    {
        _status.IsSyncing = true;
        _status.LastStartedAt = DateTime.UtcNow;
        _status.LastError = null;
        _status.ProgressMessage = "Starting…";
        _status.PercentComplete = 0;
    }

    private void FinishSync(SyncResult result)
    {
        result.CompletedAt = DateTime.UtcNow;
        _status.ProgressMessage = null;
        _status.PercentComplete = 100;

        var fingerprint = ComputeFingerprint(Plugin.Instance!.ResolvedOutputPath);
        result.Fingerprint = fingerprint;

        var config = Plugin.Instance.Configuration;
        if (!string.Equals(config.LastFingerprint, fingerprint, StringComparison.Ordinal))
        {
            config.LastFingerprint = fingerprint;
            Plugin.Instance.SaveConfiguration();
            _libraryManager.QueueLibraryScan();
            result.LibraryScanTriggered = true;
            _logger.LogInformation("Library scan queued after AIOStreams sync.");
        }

        _status.IsSyncing = false;
        _status.LastCompletedAt = result.CompletedAt;
        _status.LastResult = result;
    }

    private void FailSync(Exception ex)
    {
        _status.IsSyncing = false;
        _status.LastError = ex.Message;
        _status.ProgressMessage = null;
        _logger.LogError(ex, "AIOStreams sync failed.");
    }

    /// <summary>
    /// Computes a stable hash of the current managed file tree (relative path + content).
    /// </summary>
    private static string ComputeFingerprint(string root)
    {
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            parts.Add(Path.GetRelativePath(root, file));
            parts.Add(File.ReadAllText(file));
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", parts));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
