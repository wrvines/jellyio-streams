using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Thin HTTP client for the Stremio addon protocol as served by AIOStreams.
/// </summary>
public sealed class AIOStreamsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AIOStreamsClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AIOStreamsClient(HttpClient http, ILogger<AIOStreamsClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Fetches the addon manifest.
    /// </summary>
    public async Task<AddonManifest?> GetManifestAsync(
        string addonUrl,
        string? extraQuery,
        CancellationToken cancellationToken)
    {
        var (baseUrl, query) = Normalize(addonUrl, extraQuery);
        var url = $"{baseUrl}/manifest.json{query}";
        var manifest = await GetJsonAsync<AddonManifest>(url, cancellationToken).ConfigureAwait(false);
        if (manifest is not null)
        {
            _logger.LogDebug("Manifest loaded from {Url}: {Name} {Version}", url, manifest.Name, manifest.Version);
        }

        return manifest;
    }

    /// <summary>
    /// Fetches catalog entries. <paramref name="search"/> enables the addon's search catalog behaviour.
    /// </summary>
    public async Task<CatalogResponse?> GetCatalogAsync(
        string addonUrl,
        string? extraQuery,
        string type,
        string catalogId,
        int skip,
        int limit,
        string? search,
        CancellationToken cancellationToken)
    {
        var (baseUrl, query) = Normalize(addonUrl, extraQuery);

        var extras = new List<string>();
        if (search is not null)
        {
            extras.Add("search=" + Uri.EscapeDataString(search));
        }

        if (skip > 0)
        {
            extras.Add("skip=" + skip);
        }

        if (limit > 0)
        {
            extras.Add("limit=" + limit);
        }

        var extrasPath = extras.Count > 0 ? "/" + string.Join("&", extras) : string.Empty;
        var url = $"{baseUrl}/catalog/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(catalogId)}{extrasPath}.json{query}";
        return await GetJsonAsync<CatalogResponse>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches full metadata for a title (includes episode list for series).
    /// </summary>
    public async Task<MetaResponse?> GetMetaAsync(
        string addonUrl,
        string? extraQuery,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        var (baseUrl, query) = Normalize(addonUrl, extraQuery);
        var url = $"{baseUrl}/meta/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}.json{query}";
        return await GetJsonAsync<MetaResponse>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the stream list for a title (movies) or episode (series, id like "tt123:1:2").
    /// </summary>
    public async Task<StreamsResponse?> GetStreamsAsync(
        string addonUrl,
        string? extraQuery,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        var (baseUrl, query) = Normalize(addonUrl, extraQuery);
        var url = $"{baseUrl}/stream/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}.json{query}";
        return await GetJsonAsync<StreamsResponse>(url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AIOStreams request failed: {Url} -> {Status}", url, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AIOStreams request error: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Turns a user supplied install URL into a base URL + query suffix.
    /// Accepts ".../manifest.json", a bare install path ("https://host/stremio/&lt;uuid&gt;/&lt;token&gt;") or the instance root.
    /// </summary>
    private static (string BaseUrl, string Query) Normalize(string addonUrl, string? extraQuery)
    {
        var url = addonUrl.Trim().TrimEnd('/');

        const string manifestSuffix = "/manifest.json";
        if (url.EndsWith(manifestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^manifestSuffix.Length].TrimEnd('/');
        }

        var query = string.Empty;
        if (!string.IsNullOrWhiteSpace(extraQuery))
        {
            var extra = extraQuery.Trim();
            query = extra.StartsWith('?') ? extra : "?" + extra;
        }

        return (url, query);
    }
}
