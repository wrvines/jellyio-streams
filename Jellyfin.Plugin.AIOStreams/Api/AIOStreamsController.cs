using Jellyfin.Plugin.AIOStreams.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Api;

/// <summary>
/// Body of the Remove request.
/// </summary>
public sealed class RemoveTitleRequest
{
    public string Type { get; set; } = "movie";

    public string Title { get; set; } = string.Empty;

    public string? Year { get; set; }
}

/// <summary>
/// The managed stream folder contents.
/// </summary>
public sealed class LibraryListing
{
    public string RootPath { get; set; } = string.Empty;

    public IReadOnlyList<TitleOnDisk> Titles { get; set; } = [];
}

/// <summary>
/// Plugin status for the UI.
/// </summary>
public sealed class PluginStatus
{
    public string PluginVersion { get; set; } = string.Empty;

    public bool AddonUrlConfigured { get; set; }

    public string FolderState { get; set; } = string.Empty;

    public string StreamRoot { get; set; } = string.Empty;

    public bool QualityPickerAtAdd { get; set; }

    public string? AddonName { get; set; }
}

/// <summary>
/// REST endpoints for the plugin: status, search, stream listing, add/remove, and the
/// unauthenticated playback redirect endpoint used by .strm files.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AIOStreams")]
public class AIOStreamsController : ControllerBase
{
    private readonly AIOStreamsClient _client;
    private readonly OnDemandService _onDemand;
    private readonly ILogger<AIOStreamsController> _logger;

    public AIOStreamsController(
        AIOStreamsClient client,
        OnDemandService onDemand,
        ILogger<AIOStreamsController> logger)
    {
        _client = client;
        _onDemand = onDemand;
        _logger = logger;
    }

    /// <summary>
    /// Returns plugin status including the /data/stream validation state.
    /// </summary>
    [HttpGet("Status")]
    public async Task<ActionResult<PluginStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return BadRequest("Plugin is not loaded.");
        }

        var folderState = plugin.EnsureStreamFolder();
        var config = plugin.Configuration;
        string? addonName = null;
        if (!string.IsNullOrWhiteSpace(config.AddonUrl))
        {
            var manifest = await _client.GetManifestAsync(config.AddonUrl, config.ExtraQuery, cancellationToken).ConfigureAwait(false);
            addonName = manifest?.Name;
        }

        return Ok(new PluginStatus
        {
            PluginVersion = plugin.Version?.ToString() ?? typeof(AIOStreamsController).Assembly.GetName().Version?.ToString() ?? "unknown",
            AddonUrlConfigured = !string.IsNullOrWhiteSpace(config.AddonUrl),
            FolderState = folderState,
            StreamRoot = Plugin.StreamRoot,
            QualityPickerAtAdd = config.QualityPickerAtAdd,
            AddonName = addonName
        });
    }

    /// <summary>
    /// Searches the addon's search catalog.
    /// </summary>
    [HttpGet("Search")]
    public async Task<ActionResult<IReadOnlyList<MetaPreview>>> SearchAsync(
        [FromQuery] string term,
        [FromQuery] string type = "movie",
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("A search term is required.");
            }

            return Ok(await _onDemand.SearchAsync(term, type, limit, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Resolves and returns the playable streams for a title or episode, ranked best first.
    /// </summary>
    [HttpGet("Streams")]
    public async Task<ActionResult<IReadOnlyList<ApiStream>>> GetStreamsAsync(
        [FromQuery] string type,
        [FromQuery] string id,
        [FromQuery] int? max = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("A title id is required.");
            }

            var cap = max is > 0 ? max : Plugin.Instance?.Configuration.MaxStreamsShown;
            return Ok(await _onDemand.ResolveStreamsAsync(type, id, cap, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Adds a title to the stream library (incremental, does not wipe existing content).
    /// </summary>
    [HttpPost("Add")]
    public async Task<ActionResult<AddResult>> AddAsync(TitleAddRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                return BadRequest("Plugin is not loaded.");
            }

            var folderState = plugin.EnsureStreamFolder();
            if (folderState != FolderState.Ok.ToString())
            {
                return BadRequest($"The stream folder {Plugin.StreamRoot} is not usable (state: {folderState}). Create it or enable auto-create in the plugin settings.");
            }

            plugin.EnsurePlaybackSecret();
            return Ok(await _onDemand.AddTitleAsync(request, BuildPlaybackBaseUrl(), cancellationToken).ConfigureAwait(false));
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
    /// Removes a title's folder from the stream library.
    /// </summary>
    [HttpPost("Remove")]
    public async Task<ActionResult<bool>> RemoveAsync(RemoveTitleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("A title is required.");
            }

            return Ok(await _onDemand.RemoveTitleAsync(request.Type, request.Title, request.Year, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Lists the titles currently on disk in the stream folder.
    /// </summary>
    [HttpGet("Library")]
    public ActionResult<LibraryListing> GetLibrary()
    {
        var root = Plugin.Instance is null ? null : Plugin.StreamRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return Ok(new LibraryListing());
        }

        return Ok(new LibraryListing
        {
            RootPath = root,
            Titles = OnDemandLibrary.List(root)
        });
    }

    /// <summary>
    /// Creates the /data/stream folder (used by the "Create now" button).
    /// </summary>
    [HttpPost("CreateFolder")]
    public ActionResult<string> CreateFolder()
    {
        var root = Plugin.Instance is null ? null : Plugin.StreamRoot;
        if (string.IsNullOrEmpty(root))
        {
            return BadRequest("Plugin is not loaded.");
        }

        StreamFolder.Create(root);
        return Ok(StreamFolder.Validate(root).ToString());
    }

    /// <summary>
    /// Playback endpoint referenced by generated .strm files. Validates the HMAC token,
    /// resolves a fresh stream from AIOStreams, then redirects (or proxies when the
    /// stream needs custom request headers). Unauthenticated by design.
    /// </summary>
    [HttpGet("Stream")]
    [AllowAnonymous]
    public async Task<ActionResult> PlayAsync([FromQuery] string token, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return Unauthorized();
        }

        var secret = plugin.Configuration.PlaybackSecret;
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("Playback request with no playback secret configured.");
            return Unauthorized();
        }

        var tokenService = new PlaybackTokenService(secret);
        if (!tokenService.TryVerify(token, out var payload) || payload is null)
        {
            _logger.LogWarning("Playback request rejected: invalid token.");
            return Unauthorized();
        }

        var config = plugin.Configuration;
        var response = await _client.GetStreamsAsync(config.AddonUrl, config.ExtraQuery, payload.Type, payload.Id, cancellationToken).ConfigureAwait(false);
        var streams = (response?.Streams ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var selected = StreamResolver.Select(streams, payload.Quality);
        if (selected is null)
        {
            _logger.LogWarning("No playable stream found for {Type}/{Id}", payload.Type, payload.Id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "No playable stream was found.");
        }

        if (selected.BehaviorHints?.NotWebReady == true)
        {
            return await ProxyAsync(selected.Url!, cancellationToken).ConfigureAwait(false);
        }

        return Redirect(selected.Url!);
    }

    /// <summary>
    /// Serves the optional web-UI hook script (Custom JavaScript integration).
    /// </summary>
    [HttpGet("WebUI/hook.js")]
    [AllowAnonymous]
    public ActionResult GetHookJs()
    {
        var assembly = typeof(AIOStreamsController).Assembly;
        var resource = $"{assembly.GetName().Name}.Web.hook.js";
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "text/javascript", enableRangeProcessing: false);
    }

    private async Task<ActionResult> ProxyAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
            request.Headers.Referrer = new Uri(origin);
            using var response = await _client.SendPlaybackAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Playback proxy failed: {Url} -> {Status}", url, (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, "The stream source failed to respond.");
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return File(body, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _logger.LogWarning(ex, "Playback proxy failed for {Url}", url);
            return StatusCode(StatusCodes.Status502BadGateway, "The stream source failed to respond.");
        }
    }

    private string BuildPlaybackBaseUrl()
    {
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        return $"{scheme}://{host}";
    }
}
