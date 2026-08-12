namespace Jellyfin.Plugin.AIOStreams.Services;

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

    public bool? NotWebReady { get; set; }
}
