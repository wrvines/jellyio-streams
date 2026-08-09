using System.Text;
using System.Text.RegularExpressions;

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

/// <summary>
/// Writes the Jellyfin library structure (.strm files plus nfo sidecars) that this plugin manages.
///
/// Layout:
///   Movies/{Title (Year)}/{Title (Year)}.strm          (+ movie.nfo, versions as [label] files)
///   Shows/{Title (Year)}/tvshow.nfo
///   Shows/{Title (Year)}/Season 01/S01E01 [label].strm
/// </summary>
public static class StrmLibrary
{
    public const string MoviesDirName = "Movies";
    public const string ShowsDirName = "Shows";

    private static readonly Regex _yearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

    /// <summary>
    /// Deletes everything this plugin manages under <paramref name="root"/>.
    /// </summary>
    public static void Wipe(string root)
    {
        foreach (var dir in new[] { Path.Combine(root, MoviesDirName), Path.Combine(root, ShowsDirName) })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Writes the STRM files (and movie.nfo) for a movie. The first stream becomes the unlabelled
    /// primary version; the rest become bracketed "versions" Jellyfin groups under the same movie.
    /// </summary>
    /// <returns>The relative paths of all written files.</returns>
    public static IReadOnlyList<string> WriteMovie(
        string root,
        string title,
        string? year,
        string? imdbId,
        IReadOnlyList<StreamFile> streams,
        CancellationToken cancellationToken)
    {
        var folderName = BuildFolderName(title, year);
        var dir = Path.Combine(root, MoviesDirName, folderName);
        Directory.CreateDirectory(dir);

        var written = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < streams.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseName = i == 0 ? folderName : $"{folderName} [{SanitizeLabel(streams[i].Label)}]";
            baseName = MakeUnique(baseName, usedNames);

            var strmPath = Path.Combine(dir, baseName + ".strm");
            File.WriteAllText(strmPath, streams[i].Url);
            written.Add(Relative(root, strmPath));
        }

        var nfoPath = Path.Combine(dir, "movie.nfo");
        File.WriteAllText(nfoPath, BuildMovieNfo(title, year, imdbId));
        written.Add(Relative(root, nfoPath));

        return written;
    }

    /// <summary>
    /// Writes the tvshow.nfo plus one STRM per episode/stream for a series.
    /// </summary>
    /// <returns>The relative paths of all written files.</returns>
    public static IReadOnlyList<string> WriteShow(
        string root,
        string title,
        string? year,
        string? imdbId,
        IReadOnlyList<EpisodeGroup> episodes,
        CancellationToken cancellationToken)
    {
        var folderName = BuildFolderName(title, year);
        var showDir = Path.Combine(root, ShowsDirName, folderName);
        Directory.CreateDirectory(showDir);

        var written = new List<string>();

        var nfoPath = Path.Combine(showDir, "tvshow.nfo");
        File.WriteAllText(nfoPath, BuildTvShowNfo(title, year, imdbId));
        written.Add(Relative(root, nfoPath));

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seasonDir = Path.Combine(showDir, $"Season {episode.Season:00}");
            Directory.CreateDirectory(seasonDir);

            var episodeBase = $"S{episode.Season:00}E{episode.Episode:00}";
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < episode.Streams.Count; i++)
            {
                var baseName = i == 0 ? episodeBase : $"{episodeBase} [{SanitizeLabel(episode.Streams[i].Label)}]";
                baseName = MakeUnique(baseName, usedNames);

                var strmPath = Path.Combine(seasonDir, baseName + ".strm");
                File.WriteAllText(strmPath, episode.Streams[i].Url);
                written.Add(Relative(root, strmPath));
            }
        }

        return written;
    }

    /// <summary>
    /// Builds a folder name like "Dune (2021)" from a title and year.
    /// </summary>
    public static string BuildFolderName(string title, string? year)
    {
        var name = Sanitize(title);
        return string.IsNullOrWhiteSpace(year) ? name : $"{name} ({year})";
    }

    /// <summary>
    /// Extracts a 4-digit year from a Stremio releaseInfo value ("2021" or "2021-05-15").
    /// </summary>
    public static string? ExtractYear(string? releaseInfo)
    {
        if (string.IsNullOrWhiteSpace(releaseInfo))
        {
            return null;
        }

        var match = _yearRegex.Match(releaseInfo);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Extracts an IMDb id ("tt1234567") from a Stremio item id, or null when not present.
    /// </summary>
    public static string? ExtractImdbId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var match = Regex.Match(id, @"\btt\d{6,10}\b");
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Cleans a stream label for safe use inside a filename (no brackets, no path characters).
    /// </summary>
    public static string SanitizeLabel(string label)
    {
        var cleaned = Regex.Replace(label, @"[\[\]]", " ");
        return Sanitize(cleaned);
    }

    private static string BuildMovieNfo(string title, string? year, string? imdbId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<movie>");
        sb.AppendLine($"  <title>{EscapeXml(title)}</title>");
        if (!string.IsNullOrWhiteSpace(year))
        {
            sb.AppendLine($"  <year>{EscapeXml(year)}</year>");
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            sb.AppendLine($"  <uniqueid type=\"imdb\">{EscapeXml(imdbId)}</uniqueid>");
        }

        sb.AppendLine("</movie>");
        return sb.ToString();
    }

    private static string BuildTvShowNfo(string title, string? year, string? imdbId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<tvshow>");
        sb.AppendLine($"  <title>{EscapeXml(title)}</title>");
        if (!string.IsNullOrWhiteSpace(year))
        {
            sb.AppendLine($"  <year>{EscapeXml(year)}</year>");
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            sb.AppendLine($"  <uniqueid type=\"imdb\">{EscapeXml(imdbId)}</uniqueid>");
        }

        sb.AppendLine("</tvshow>");
        return sb.ToString();
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? ' ' : c);
        }

        var result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().Trim('.', ' ');
        return result.Length > 120 ? result[..120] : result;
    }

    private static string MakeUnique(string baseName, ISet<string> usedNames)
    {
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path);
}
