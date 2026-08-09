namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// DTOs for the Stremio addon protocol as implemented by AIOStreams.
/// All properties are matched case-insensitively against the JSON payload.
/// </summary>
public sealed class AddonManifest
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Version { get; set; }

    public string? Description { get; set; }

    public ManifestResource[]? Resources { get; set; }

    public ManifestCatalog[]? Catalogs { get; set; }

    public string[]? Types { get; set; }

    public ManifestBehaviorHints? BehaviorHints { get; set; }
}

public sealed class ManifestResource
{
    public string? Name { get; set; }

    public string[]? Types { get; set; }

    public string[]? IdPrefixes { get; set; }
}

public sealed class ManifestCatalog
{
    public string? Type { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public bool? Featured { get; set; }

    public ManifestCatalogExtra[]? Extra { get; set; }
}

public sealed class ManifestCatalogExtra
{
    public string? Name { get; set; }

    public string[]? Options { get; set; }

    public bool IsRequired { get; set; }
}

public sealed class ManifestBehaviorHints
{
    public bool? Configurable { get; set; }

    public bool? ConfigurationRequired { get; set; }
}

public sealed class CatalogResponse
{
    public MetaPreview[]? Metas { get; set; }
}

public class MetaPreview
{
    public string? Id { get; set; }

    public string? Type { get; set; }

    public string? Name { get; set; }

    public string? Poster { get; set; }

    public string? PosterShape { get; set; }

    public string? Background { get; set; }

    public string? Logo { get; set; }

    public string? Description { get; set; }

    public string? ImdbRating { get; set; }

    public string? ReleaseInfo { get; set; }

    public string? Runtime { get; set; }

    public string[]? Genres { get; set; }
}

public sealed class MetaResponse
{
    public MetaFull? Meta { get; set; }
}

public sealed class MetaFull : MetaPreview
{
    public MetaVideo[]? Videos { get; set; }

    public string? Language { get; set; }

    public string? Country { get; set; }
}

public sealed class MetaVideo
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? Name { get; set; }

    public string? Released { get; set; }

    public string? Thumbnail { get; set; }

    public string? Overview { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }
}

public sealed class StreamsResponse
{
    public StreamResult[]? Streams { get; set; }
}

public sealed class StreamResult
{
    public string? Url { get; set; }

    public string? NzbUrl { get; set; }

    public string? InfoHash { get; set; }

    public int? FileIdx { get; set; }

    public string? Name { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? ExternalUrl { get; set; }

    public StreamBehaviorHints? BehaviorHints { get; set; }

    public StreamSubtitle[]? Subtitles { get; set; }
}

public sealed class StreamBehaviorHints
{
    public bool? NotWebReady { get; set; }

    public string? BingeGroup { get; set; }

    public string? Filename { get; set; }

    public long? VideoSize { get; set; }
}

public sealed class StreamSubtitle
{
    public string? Id { get; set; }

    public string? Url { get; set; }

    public string? Lang { get; set; }
}
