using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Ranks and selects AIOStreams streams by quality (Stremio-style "pick the best").
/// Pure BCL — fully unit-testable.
/// </summary>
public static partial class StreamResolver
{
    [GeneratedRegex(@"\b(4320|2160|1080|720|480|360)p\b|\b(4k|uhd)\b|\b8k\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityRegex();

    /// <summary>
    /// Returns the resolution family ("2160p", "1080p", ...) found in the text, or null.
    /// </summary>
    public static string? ResolveQuality(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = QualityRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Value;
        return value.ToLowerInvariant() switch
        {
            "4k" or "uhd" => "2160p",
            "8k" => "4320p",
            _ => value + "p"
        };
    }

    /// <summary>
    /// Ranks streams best-first by resolution, then HDR, then file size. Stable.
    /// </summary>
    public static IReadOnlyList<StreamResult> Rank(IEnumerable<StreamResult> streams)
    {
        var result = new List<StreamResult>(streams);
        var scores = new Dictionary<StreamResult, long>();
        foreach (var stream in result)
        {
            scores[stream] = Score(stream);
        }

        result.Sort((a, b) => scores[b].CompareTo(scores[a]));
        return result;
    }

    /// <summary>
    /// Selects the stream to play. "auto"/null picks the top-ranked stream;
    /// a quality family ("1080p") picks the best stream of that family, falling
    /// back to the top-ranked stream when none matches.
    /// </summary>
    public static StreamResult? Select(IEnumerable<StreamResult> streams, string? quality)
    {
        var ranked = Rank(streams);
        if (ranked.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(quality) || quality.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return ranked[0];
        }

        var family = quality.StartsWith("43", StringComparison.OrdinalIgnoreCase) ? "4320p"
            : quality.StartsWith("21", StringComparison.OrdinalIgnoreCase) ? "2160p"
            : quality.StartsWith("108", StringComparison.OrdinalIgnoreCase) ? "1080p"
            : quality.StartsWith("72", StringComparison.OrdinalIgnoreCase) ? "720p"
            : null;

        if (family is not null)
        {
            var match = ranked.FirstOrDefault(s =>
                string.Equals(ResolveQuality((s.Title ?? s.Name) + " " + (s.Description ?? string.Empty)), family, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return ranked[0];
    }

    private static long Score(StreamResult stream)
    {
        var label = (stream.Title ?? string.Empty) + " " + (stream.Name ?? string.Empty) + " " + (stream.Description ?? string.Empty);
        var quality = ResolveQuality(label);
        var resolution = quality switch
        {
            "4320p" => 4320,
            "2160p" => 2160,
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            "360p" => 360,
            _ => 0
        };

        long score = resolution * 1000L;
        var upper = label.ToUpperInvariant();
        if (upper.Contains("DOLBY VISION") || upper.Contains("DV"))
        {
            score += 200;
        }
        else if (upper.Contains("HDR10+"))
        {
            score += 150;
        }
        else if (upper.Contains("HDR"))
        {
            score += 100;
        }

        var size = stream.BehaviorHints?.VideoSize ?? 0;
        if (size > 0)
        {
            score += Math.Min(45, (long)Math.Log2(size));
        }

        return score;
    }
}
