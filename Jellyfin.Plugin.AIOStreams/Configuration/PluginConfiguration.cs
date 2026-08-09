using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AIOStreams.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the AIOStreams addon URL.
    /// Accepts "https://host", "https://host/stremio/&lt;uuid&gt;/&lt;token&gt;" or a full ".../manifest.json" URL.
    /// </summary>
    public string AddonUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional query parameters appended to every addon request.
    /// </summary>
    public string ExtraQuery { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder that will hold the generated .strm files.
    /// Add this folder (or its Movies/Shows subfolders) as a Jellyfin library.
    /// Empty means the default folder under the Jellyfin data directory.
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets catalog ids (comma separated) to sync. Empty = all movie/series catalogs from the manifest.
    /// </summary>
    public string EnabledCatalogIds { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of titles fetched per catalog. Zero = unlimited.
    /// </summary>
    public int MaxItemsPerCatalog { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum number of streams written per title (as Jellyfin "versions"). Zero = all streams.
    /// </summary>
    public int MaxStreamsPerTitle { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether episodes of series are resolved and written.
    /// </summary>
    public bool SyncEpisodes { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of episodes resolved per series (most recent first). Zero = all episodes.
    /// </summary>
    public int MaxEpisodesPerSeries { get; set; } = 0;

    /// <summary>
    /// Gets or sets the scheduled refresh interval in hours. Zero = manual refresh only.
    /// </summary>
    public int RefreshIntervalHours { get; set; } = 6;

    /// <summary>
    /// Gets or sets the fingerprint of the last successful sync. Used to skip library rescans when nothing changed.
    /// </summary>
    public string LastFingerprint { get; set; } = string.Empty;
}
