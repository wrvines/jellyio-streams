using Jellyfin.Plugin.AIOStreams.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Api;

/// <summary>
/// A stream as exposed to the plugin UI.
/// </summary>
public sealed class ApiStream
{
    public string? Url { get; set; }

    public string? Label { get; set; }

    public string? Title { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int? FileIdx { get; set; }

    public bool? NotWebReady { get; set; }
}

/// <summary>
/// Body of the Add request.
/// </summary>
public sealed class AddTitleRequest
{
    public string Type { get; set; } = "movie";

    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ReleaseInfo { get; set; }

    public int? MaxStreams { get; set; }
}

/// <summary>
/// A title currently on disk in the managed library folder.
/// </summary>
public sealed class LibraryTitle
{
    public string Name { get; set; } = string.Empty;

    public string? Year { get; set; }

    public string Type { get; set; } = "movie";
}

/// <summary>
/// The managed library folder contents.
/// </summary>
public sealed class LibraryListing
{
    public string RootPath { get; set; } = string.Empty;

    public IReadOnlyList<LibraryTitle> Movies { get; set; } = [];

    public IReadOnlyList<LibraryTitle> Shows { get; set; } = [];
}

/// <summary>
/// REST endpoints for the plugin (manifest info, search, stream listing, add/sync/status).
/// </summary>
[ApiController]
[Authorize]
[Route("AIOStreams")]
public class AIOStreamsController : ControllerBase
{
    private readonly AIOStreamsClient _client;
    private readonly CatalogSynchronizer _synchronizer;
    private readonly ILogger<AIOStreamsController> _logger;

    public AIOStreamsController(
        AIOStreamsClient client,
        CatalogSynchronizer synchronizer,
        ILogger<AIOStreamsController> logger)
    {
        _client = client;
        _synchronizer = synchronizer;
        _logger = logger;
    }

    /// <summary>
    /// Fetches and returns the AIOStreams manifest (name, version, available catalogs).
    /// </summary>
    [HttpGet("Manifest")]
    public async Task<ActionResult<AddonManifest>> GetManifestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (addonUrl, extraQuery) = RequireConnection();
            var manifest = await _client.GetManifestAsync(addonUrl, extraQuery, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                return BadRequest("Could not fetch the AIOStreams manifest. Check the addon URL.");
            }

            return Ok(manifest);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Searches the addon's search catalog.
    /// </summary>
    [HttpGet("Search")]
    public async Task<ActionResult<IReadOnlyList<MetaPreview>>> SearchAsync(
        [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string term,
        [FromQuery] string type = "movie",
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (addonUrl, extraQuery) = RequireConnection();

            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("A search term is required.");
            }

            var manifest = await _client.GetManifestAsync(addonUrl, extraQuery, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                return BadRequest("Could not fetch the AIOStreams manifest.");
            }

            var searchCatalog = (manifest.Catalogs ?? [])
                .FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)
                    && c.Id?.Contains("search", StringComparison.OrdinalIgnoreCase) == true);

            if (searchCatalog is null)
            {
                return Ok(Array.Empty<MetaPreview>());
            }

            var response = await _client.GetCatalogAsync(
                    addonUrl,
                    extraQuery,
                    type,
                    searchCatalog.Id!,
                    0,
                    Math.Clamp(limit, 1, 100),
                    term,
                    cancellationToken)
                .ConfigureAwait(false);

            var results = (response?.Metas ?? [])
                .Where(m => !string.Equals(m.Type, "error", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Ok(results);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Resolves and returns the playable streams for a title or episode.
    /// </summary>
    [HttpGet("Streams")]
    public async Task<ActionResult<IReadOnlyList<ApiStream>>> GetStreamsAsync(
        [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string type,
        [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (addonUrl, extraQuery) = RequireConnection();

            var response = await _client.GetStreamsAsync(addonUrl, extraQuery, type, id, cancellationToken).ConfigureAwait(false);

            var streams = (response?.Streams ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.Url))
                .Select((s, i) => new ApiStream
                {
                    Url = s.Url,
                    Label = s.Title ?? s.Name ?? $"Stream {i + 1}",
                    Title = s.Title,
                    Name = s.Name,
                    Description = s.Description,
                    FileIdx = s.FileIdx,
                    NotWebReady = s.BehaviorHints?.NotWebReady
                })
                .ToList();

            return Ok(streams);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Adds a single title to the library (incremental, does not wipe existing content).
    /// </summary>
    [HttpPost("Add")]
    public async Task<ActionResult<SyncResult>> AddAsync(AddTitleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _synchronizer.AddTitleAsync(new SingleTitleRequest
            {
                Type = request.Type,
                Id = request.Id,
                Name = request.Name,
                ReleaseInfo = request.ReleaseInfo,
                MaxStreams = request.MaxStreams
            }, cancellationToken).ConfigureAwait(false);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Runs a full catalog sync. This may take a while.
    /// </summary>
    [HttpPost("Sync")]
    public async Task<ActionResult<SyncResult>> SyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _synchronizer.SyncCatalogsAsync(progress: null, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Returns the current sync status and the result of the last operation.
    /// </summary>
    [HttpGet("Status")]
    public ActionResult<SyncStatus> GetStatusAsync()
    {
        var status = _synchronizer.GetStatus();
        status.PluginVersion = Plugin.Instance?.Version?.ToString() ?? typeof(AIOStreamsController).Assembly.GetName().Version?.ToString();
        status.AddonUrlConfigured = !string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.AddonUrl);
        return Ok(status);
    }

    /// <summary>
    /// Lists the titles currently on disk in the managed library folder.
    /// </summary>
    [HttpGet("Library")]
    public ActionResult<LibraryListing> GetLibraryAsync()
    {
        var root = Plugin.Instance?.ResolvedOutputPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return Ok(new LibraryListing());
        }

        return Ok(new LibraryListing
        {
            RootPath = root,
            Movies = ListTitles(Path.Combine(root, StrmLibrary.MoviesDirName), "movie"),
            Shows = ListTitles(Path.Combine(root, StrmLibrary.ShowsDirName), "series")
        });
    }

    private static IReadOnlyList<LibraryTitle> ListTitles(string dir, string type)
    {
        var result = new List<LibraryTitle>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        foreach (var folder in Directory.EnumerateDirectories(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(folder);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var year = (string?)null;
            var name = folderName;
            var match = System.Text.RegularExpressions.Regex.Match(folderName, @"^(.*?)(?:\s*\((\d{4})\))?$");
            if (match.Success)
            {
                name = match.Groups[1].Value.Trim();
                year = match.Groups[2].Success ? match.Groups[2].Value : null;
            }

            result.Add(new LibraryTitle { Name = name, Year = year, Type = type });
        }

        return result;
    }

    private (string AddonUrl, string? ExtraQuery) RequireConnection()
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin is not loaded.");

        if (string.IsNullOrWhiteSpace(config.AddonUrl))
        {
            var version = Plugin.Instance?.Version?.ToString() ?? typeof(AIOStreamsController).Assembly.GetName().Version?.ToString() ?? "unknown";
            throw new InvalidOperationException(
                "The AIOStreams addon URL is not configured (plugin " + version + "). "
                + "Open the plugin settings, enter the URL, and press Save before using Test connection.");
        }

        return (config.AddonUrl.Trim(), string.IsNullOrWhiteSpace(config.ExtraQuery) ? null : config.ExtraQuery.Trim());
    }
}
