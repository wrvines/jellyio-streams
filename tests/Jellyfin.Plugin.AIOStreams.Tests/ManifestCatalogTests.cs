using Jellyfin.Plugin.AIOStreams.Services;
using Xunit;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public sealed class ManifestCatalogTests
{
    [Fact]
    public void SupportsSearch_UsesDeclaredSearchExtra()
    {
        var catalog = new ManifestCatalog
        {
            Id = "aiostreams.merged.movies",
            Type = "movie",
            Extra = [new ManifestCatalogExtra { Name = "search" }]
        };

        var property = typeof(ManifestCatalog).GetProperty("SupportsSearch");

        Assert.NotNull(property);
        Assert.True((bool)property.GetValue(catalog)!);
    }

    [Fact]
    public void SupportsSearch_RetainsSearchIdFallback()
    {
        var catalog = new ManifestCatalog { Id = "legacy-search", Type = "movie" };

        Assert.True(catalog.SupportsSearch);
    }
}
