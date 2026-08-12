using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Result of validating the required /data/stream folder.
/// </summary>
public enum FolderState
{
    Ok,
    Missing,
    NotDirectory,
    NotWritable
}

/// <summary>
/// Validation, creation and TRaSH-style path building for the required stream folder.
/// Pure BCL — fully unit-testable.
/// </summary>
public static partial class StreamFolder
{
    /// <summary>
    /// Gets the TRaSH "category" folder for movies.
    /// </summary>
    public const string MoviesDirName = "movies";

    /// <summary>
    /// Gets the TRaSH "category" folder for TV shows.
    /// </summary>
    public const string TvDirName = "tv";

    private const string ProbeFileName = ".jellyio-probe";

    private static readonly char[] _invalidFileNameChars = "<>:\"/\\|?*"
        .Concat(Path.GetInvalidFileNameChars())
        .Distinct()
        .ToArray();

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\btt\d{6,10}\b")]
    private static partial Regex ImdbRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Validates that <paramref name="root"/> exists, is a directory, and is writable.
    /// </summary>
    public static FolderState Validate(string root)
    {
        if (!Directory.Exists(root))
        {
            return File.Exists(root) ? FolderState.NotDirectory : FolderState.Missing;
        }

        if (File.Exists(root))
        {
            return FolderState.NotDirectory;
        }

        var probe = Path.Combine(root, ProbeFileName);
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return FolderState.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FolderState.NotWritable;
        }
    }

    /// <summary>
    /// Creates the root and the TRaSH category subfolders. Idempotent.
    /// </summary>
    public static void Create(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, MoviesDirName));
        Directory.CreateDirectory(Path.Combine(root, TvDirName));
    }

    /// <summary>
    /// Builds a folder name like "Dune (2021)" from a title and year.
    /// </summary>
    public static string BuildFolderName(string title, string? year)
    {
        var name = SanitizeTitle(title);
        return string.IsNullOrWhiteSpace(year) ? name : $"{name} ({year})";
    }

    /// <summary>
    /// Cleans a title for safe use as a folder name.
    /// </summary>
    public static string SanitizeTitle(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(_invalidFileNameChars, c) >= 0 ? ' ' : c);
        }

        var result = WhitespaceRegex().Replace(sb.ToString(), " ").Trim().Trim('.', ' ');
        return result.Length > 120 ? result[..120] : result;
    }

    /// <summary>
    /// Gets the movie folder path for a title under the root.
    /// </summary>
    public static string MovieDir(string root, string title, string? year)
        => Path.Combine(root, MoviesDirName, BuildFolderName(title, year));

    /// <summary>
    /// Gets the TV show folder path for a title under the root.
    /// </summary>
    public static string TvShowDir(string root, string title, string? year)
        => Path.Combine(root, TvDirName, BuildFolderName(title, year));

    /// <summary>
    /// Builds a Jellyfin episode file name like "S01E02.strm".
    /// </summary>
    public static string EpisodeFileName(int season, int episode)
        => $"S{season:00}E{episode:00}.strm";

    /// <summary>
    /// Extracts a 4-digit year from a Stremio releaseInfo value, or null.
    /// </summary>
    public static string? ExtractYear(string? releaseInfo)
    {
        if (string.IsNullOrWhiteSpace(releaseInfo))
        {
            return null;
        }

        var match = YearRegex().Match(releaseInfo);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Extracts an IMDb id ("tt1234567") from a Stremio item id, or null.
    /// </summary>
    public static string? ExtractImdbId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var match = ImdbRegex().Match(id);
        return match.Success ? match.Value : null;
    }
}
