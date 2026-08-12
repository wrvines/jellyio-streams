using Jellyfin.Plugin.AIOStreams.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Request to add a single title to the stream library.
/// </summary>
public sealed class TitleAddRequest
{
    public string Type { get; set; } = "movie";

    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ReleaseInfo { get; set; }

    /// <summary>
    /// Gets or sets the desired quality ("auto", "2160p", ...). Falls back to the plugin DefaultQuality.
    /// </summary>
    public string? Quality { get; set; }
}

/// <summary>
/// Result of an add operation.
/// </summary>
public sealed record AddResult(int Movies, int Shows, int Episodes, int Streams, int Files);

/// <summary>
/// On-demand orchestration: search the addon, add titles to /data/stream as signed .strm files,
/// and remove them again. Each add triggers an incremental library scan via ILibraryMonitor.
/// </summary>
public sealed class OnDemandService
{
    private readonly AIOStreamsClient _client;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<OnDemandService> _logger;
    private readonly SemaphoreSlim _addLock = new(1, 1);

    public OnDemandService(AIOStreamsClient client, ILibraryMonitor libraryMonitor, ILogger<OnDemandService> logger)
    {
        _client = client;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Searches the addon's search catalog.
    /// </summary>
    public async Task<IReadOnlyList<MetaPreview>> SearchAsync(string term, string type, int limit, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var manifest = await _client.GetManifestAsync(config.AddonUrl, config.ExtraQuery, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not fetch the AIOStreams manifest.");

        var searchCatalog = (manifest.Catalogs ?? [])
            .FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)
                && c.Id?.Contains("search", StringComparison.OrdinalIgnoreCase) == true);

        if (searchCatalog is null)
        {
            return Array.Empty<MetaPreview>();
        }

        var response = await _client.GetCatalogAsync(
                config.AddonUrl,
                config.ExtraQuery,
                type,
                searchCatalog.Id!,
                0,
                Math.Clamp(limit, 1, 100),
                term,
                cancellationToken)
            .ConfigureAwait(false);

        return (response?.Metas ?? [])
            .Where(m => !string.Equals(m.Type, "error", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Resolves the playable streams for a title/episode, ranked best first.
    /// </summary>
    public async Task<IReadOnlyList<ApiStream>> ResolveStreamsAsync(string type, string id, int? max, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var response = await _client.GetStreamsAsync(config.AddonUrl, config.ExtraQuery, type, id, cancellationToken).ConfigureAwait(false);

        var ranked = StreamResolver.Rank((response?.Streams ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()));

        var result = new List<ApiStream>();
        foreach (var stream in ranked)
        {
            if (max is > 0 && result.Count >= max)
            {
                break;
            }

            result.Add(new ApiStream
            {
                Url = stream.Url,
                Label = stream.Title ?? stream.Name ?? $"Stream {result.Count + 1}",
                Title = stream.Title,
                Name = stream.Name,
                Description = stream.Description,
                Quality = StreamResolver.ResolveQuality(stream.Title + " " + stream.Name),
                NotWebReady = stream.BehaviorHints?.NotWebReady
            });
        }

        return result;
    }

    /// <summary>
    /// Adds a title (movie or series) to the stream library: verifies streams exist,
    /// writes signed .strm files, and triggers an incremental library scan.
    /// </summary>
    public async Task<AddResult> AddTitleAsync(TitleAddRequest request, string playbackBaseUrl, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new ArgumentException("A title id is required.");
        }

        var root = Plugin.Instance is not null
            ? Plugin.StreamRoot
            : throw new InvalidOperationException("Plugin is not loaded.");

        var type = request.Type.Equals("series", StringComparison.OrdinalIgnoreCase) ? "series" : "movie";
        var quality = string.IsNullOrWhiteSpace(request.Quality) ? config.DefaultQuality : request.Quality.Trim();

        await _addLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var meta = await _client.GetMetaAsync(config.AddonUrl, config.ExtraQuery, type, request.Id, cancellationToken).ConfigureAwait(false);
            var title = !string.IsNullOrWhiteSpace(request.Name)
                ? request.Name
                : meta?.Meta?.Name ?? request.Id;
            var releaseInfo = request.ReleaseInfo ?? meta?.Meta?.ReleaseInfo;
            var year = StreamFolder.ExtractYear(releaseInfo);
            var imdbId = StreamFolder.ExtractImdbId(request.Id);

            AddResult result;
            if (type == "series")
            {
                result = await AddSeriesAsync(config, root, title, year, imdbId, quality, playbackBaseUrl, meta?.Meta, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await AddMovieAsync(config, root, title, year, imdbId, request.Id, quality, playbackBaseUrl, cancellationToken).ConfigureAwait(false);
            }

            _libraryMonitor.ReportFileSystemChanged(root);
            return result;
        }
        finally
        {
            _addLock.Release();
        }
    }

    /// <summary>
    /// Removes a title's folder from the stream library and triggers a scan.
    /// </summary>
    public async Task<bool> RemoveTitleAsync(string type, string title, string? year, CancellationToken cancellationToken)
    {
        await _addLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Plugin.Instance is not null
                ? Plugin.StreamRoot
                : throw new InvalidOperationException("Plugin is not loaded.");
            var removed = OnDemandLibrary.RemoveTitle(root, type, title, year);
            if (removed)
            {
                _libraryMonitor.ReportFileSystemChanged(root);
            }

            return removed;
        }
        finally
        {
            _addLock.Release();
        }
    }

    private async Task<AddResult> AddMovieAsync(
        PluginConfiguration config,
        string root,
        string title,
        string? year,
        string? imdbId,
        string rawId,
        string quality,
        string playbackBaseUrl,
        CancellationToken cancellationToken)
    {
        var streams = await ResolveStreamsAsync("movie", rawId, null, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
        {
            throw new InvalidOperationException("No playable streams were found for this title.");
        }

        var token = BuildToken(config, "movie", rawId, quality);
        var playbackUrl = $"{playbackBaseUrl}/AIOStreams/Stream?token={Uri.EscapeDataString(token)}";
        var written = await OnDemandLibrary.WriteMovieAsync(root, title, year, imdbId, playbackUrl, cancellationToken).ConfigureAwait(false);
        return new AddResult(Movies: 1, Shows: 0, Episodes: 0, Streams: written.Strms, Files: written.Files);
    }

    private async Task<AddResult> AddSeriesAsync(
        PluginConfiguration config,
        string root,
        string title,
        string? year,
        string? imdbId,
        string quality,
        string playbackBaseUrl,
        MetaFull? meta,
        CancellationToken cancellationToken)
    {
        var videos = meta?.Videos ?? [];
        var episodes = new List<EpisodeEntry>();
        var skipped = 0;

        foreach (var video in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(video.Id))
            {
                continue;
            }

            var streams = await ResolveStreamsAsync("series", video.Id, null, cancellationToken).ConfigureAwait(false);
            if (streams.Count == 0)
            {
                skipped++;
                continue;
            }

            var token = BuildToken(config, "series", video.Id, quality);
            episodes.Add(new EpisodeEntry(
                video.Season ?? 1,
                video.Episode ?? 1,
                $"{playbackBaseUrl}/AIOStreams/Stream?token={Uri.EscapeDataString(token)}"));
        }

        if (episodes.Count == 0)
        {
            throw new InvalidOperationException("No playable streams were found for this series.");
        }

        if (skipped > 0)
        {
            _logger.LogInformation("Skipped {Skipped} episodes without playable streams.", skipped);
        }

        var written = await OnDemandLibrary.WriteShowAsync(root, title, year, imdbId, episodes, cancellationToken).ConfigureAwait(false);
        return new AddResult(Movies: 0, Shows: 1, Episodes: episodes.Count, Streams: written.Strms, Files: written.Files);
    }

    private static string BuildToken(PluginConfiguration config, string type, string id, string quality)
        => new PlaybackTokenService(config.PlaybackSecret).IssueToken(type, id, quality);

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
}
