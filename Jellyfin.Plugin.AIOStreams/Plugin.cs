using Jellyfin.Plugin.AIOStreams.Configuration;
using Jellyfin.Plugin.AIOStreams.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AIOStreams;

/// <summary>
/// AIOStreams plugin: a Stremio-like on-demand experience inside Jellyfin,
/// backed by a required /data/stream folder of .strm files.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly Guid _id = new("3a9f7c1e-5b6d-4e8f-9c2a-1d4b5e6f7a8b");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    public override Guid Id => _id;

    public override string Name => "Jellyio Streams";

    public override string Description => "Stream Stremio-addon content from your self-hosted AIOStreams instance.";

    /// <summary>
    /// Gets the required TRaSH-style folder that holds the generated .strm library.
    /// </summary>
    public static string StreamRoot => "/data/stream";

    /// <summary>
    /// Ensures a playback HMAC secret exists, generating and saving one when missing.
    /// </summary>
    public void EnsurePlaybackSecret()
    {
        if (!string.IsNullOrEmpty(Configuration.PlaybackSecret))
        {
            return;
        }

        Configuration.PlaybackSecret = PlaybackTokenService.GenerateSecret();
        SaveConfiguration();
    }

    /// <summary>
    /// Ensures the stream folder exists when auto-create is enabled. Returns the current folder state.
    /// </summary>
    public string EnsureStreamFolder()
    {
        return StreamFolder.EnsureUsable(StreamRoot, Configuration.AutoCreateStreamFolder).ToString();
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "Jellyio Streams",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "JellyioStreamsSearch",
                DisplayName = "Jellyio Streams",
                EmbeddedResourcePath = GetType().Namespace + ".Web.searchPage.html",
                EnableInMainMenu = true
            }
        };
    }
}
