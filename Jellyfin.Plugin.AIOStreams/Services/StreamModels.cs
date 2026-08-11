namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// A single stream that will be written as one .strm file (a Jellyfin "version").
/// </summary>
public sealed class StreamFile
{
    public StreamFile(string url, string label)
    {
        Url = url;
        Label = label;
    }

    public string Url { get; }

    /// <summary>
    /// Gets the human readable quality label (e.g. "2160p HDR10+ WEB-DL").
    /// </summary>
    public string Label { get; }
}

/// <summary>
/// One episode of a series with its resolved streams.
/// </summary>
public sealed class EpisodeGroup
{
    public EpisodeGroup(int? season, int? episode, string episodeTitle, IReadOnlyList<StreamFile> streams)
    {
        Season = season ?? 1;
        Episode = episode ?? 1;
        EpisodeTitle = episodeTitle;
        Streams = streams;
    }

    public int Season { get; }

    public int Episode { get; }

    public string EpisodeTitle { get; }

    public IReadOnlyList<StreamFile> Streams { get; }
}
