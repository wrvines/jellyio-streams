using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// One episode of a series to write as a single .strm file.
/// </summary>
public sealed record EpisodeEntry(int Season, int Episode, string PlaybackUrl);

/// <summary>
/// Counts of files written by a library operation.
/// </summary>
public sealed record WrittenFiles(int Strms, int Files);

/// <summary>
/// A title currently on disk in the managed stream folder.
/// </summary>
public sealed record TitleOnDisk(string Name, string? Year, string Type);

/// <summary>
/// Writes .strm files (pointing at the plugin playback endpoint) plus nfo sidecars
/// into the required /data/stream folder using the TRaSH layout. Pure BCL — fully unit-testable.
/// </summary>
public static partial class OnDemandLibrary
{
    [GeneratedRegex(@"^(.*?)(?:\s*\((\d{4})\))?\s*$")]
    private static partial Regex TitleFolderRegex();

    /// <summary>
    /// Writes the movie .strm (containing <paramref name="playbackUrl"/>) plus movie.nfo.
    /// </summary>
    public static async Task<WrittenFiles> WriteMovieAsync(
        string root,
        string title,
        string? year,
        string? imdbId,
        string playbackUrl,
        CancellationToken cancellationToken)
    {
        var folderName = StreamFolder.BuildFolderName(title, year);
        var dir = StreamFolder.MovieDir(root, title, year);
        Directory.CreateDirectory(dir);

        var strmPath = Path.Combine(dir, folderName + ".strm");
        await File.WriteAllTextAsync(strmPath, playbackUrl, cancellationToken).ConfigureAwait(false);

        var nfoPath = Path.Combine(dir, "movie.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildNfo("movie", title, year, imdbId), cancellationToken).ConfigureAwait(false);

        return new WrittenFiles(Strms: 1, Files: 2);
    }

    /// <summary>
    /// Writes tvshow.nfo plus one .strm per episode (S01E01.strm under "Season 01").
    /// </summary>
    public static async Task<WrittenFiles> WriteShowAsync(
        string root,
        string title,
        string? year,
        string? imdbId,
        IReadOnlyList<EpisodeEntry> episodes,
        CancellationToken cancellationToken)
    {
        var showDir = StreamFolder.TvShowDir(root, title, year);
        Directory.CreateDirectory(showDir);

        var nfoPath = Path.Combine(showDir, "tvshow.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildNfo("tvshow", title, year, imdbId), cancellationToken).ConfigureAwait(false);

        var strms = 0;
        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seasonDir = Path.Combine(showDir, $"Season {episode.Season:00}");
            Directory.CreateDirectory(seasonDir);

            var strmPath = Path.Combine(seasonDir, StreamFolder.EpisodeFileName(episode.Season, episode.Episode));
            await File.WriteAllTextAsync(strmPath, episode.PlaybackUrl, cancellationToken).ConfigureAwait(false);
            strms++;
        }

        return new WrittenFiles(Strms: strms, Files: strms + 1);
    }

    /// <summary>
    /// Deletes the folder for a movie or series title. Returns true when something was deleted.
    /// </summary>
    public static bool RemoveTitle(string root, string type, string title, string? year)
    {
        var dir = string.Equals(type, "series", StringComparison.OrdinalIgnoreCase)
            ? StreamFolder.TvShowDir(root, title, year)
            : StreamFolder.MovieDir(root, title, year);

        if (!Directory.Exists(dir))
        {
            return false;
        }

        Directory.Delete(dir, recursive: true);
        return true;
    }

    /// <summary>
    /// Lists the titles currently on disk, parsing "Name (Year)" folder names.
    /// </summary>
    public static IReadOnlyList<TitleOnDisk> List(string root)
    {
        var result = new List<TitleOnDisk>();
        result.AddRange(ListCategory(Path.Combine(root, StreamFolder.MoviesDirName), "movie"));
        result.AddRange(ListCategory(Path.Combine(root, StreamFolder.TvDirName), "series"));
        return result;
    }

    private static IReadOnlyList<TitleOnDisk> ListCategory(string dir, string type)
    {
        var result = new List<TitleOnDisk>();
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

            var match = TitleFolderRegex().Match(folderName);
            if (!match.Success)
            {
                continue;
            }

            result.Add(new TitleOnDisk(
                match.Groups[1].Value.Trim(),
                match.Groups[2].Success ? match.Groups[2].Value : null,
                type));
        }

        return result;
    }

    private static string BuildNfo(string rootElement, string title, string? year, string? imdbId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<{rootElement}>");
        sb.AppendLine($"  <title>{EscapeXml(title)}</title>");
        if (!string.IsNullOrWhiteSpace(year))
        {
            sb.AppendLine($"  <year>{EscapeXml(year)}</year>");
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            sb.AppendLine($"  <uniqueid type=\"imdb\">{EscapeXml(imdbId)}</uniqueid>");
        }

        sb.AppendLine($"</{rootElement}>");
        return sb.ToString();
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
}
