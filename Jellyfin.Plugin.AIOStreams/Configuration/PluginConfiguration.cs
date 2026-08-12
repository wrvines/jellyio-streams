using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AIOStreams.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the AIOStreams install URL.
    /// </summary>
    public string AddonUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional query parameters appended to every addon request.
    /// </summary>
    public string ExtraQuery { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the plugin may create /data/stream itself when missing.
    /// </summary>
    public bool AutoCreateStreamFolder { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the search UI shows a quality picker when adding.
    /// </summary>
    public bool QualityPickerAtAdd { get; set; }

    /// <summary>
    /// Gets or sets the preferred quality when the picker is off ("auto", "2160p", "1080p", "720p").
    /// </summary>
    public string DefaultQuality { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the maximum number of streams shown in the quality picker.
    /// </summary>
    public int MaxStreamsShown { get; set; } = 10;

    /// <summary>
    /// Gets or sets the HMAC secret used to sign playback tokens. Generated automatically; never displayed.
    /// </summary>
    public string PlaybackSecret { get; set; } = string.Empty;
}
