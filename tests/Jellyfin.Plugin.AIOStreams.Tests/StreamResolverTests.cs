using Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class StreamResolverTests
{
    private static StreamResult Stream(string title, long? size = null)
        => new()
        {
            Title = title,
            BehaviorHints = size is null ? null : new StreamBehaviorHints { VideoSize = size }
        };

    [Theory]
    [InlineData("Dune 2160p HDR10+ WEB-DL", "2160p")]
    [InlineData("Dune 1080p", "1080p")]
    [InlineData("Dune 4k UHD REMUX", "2160p")]
    [InlineData("Dune 8K DV", "4320p")]
    [InlineData("Dune", null)]
    public void ResolveQuality_DetectsResolution(string text, string? expected)
    {
        Assert.Equal(expected, StreamResolver.ResolveQuality(text));
    }

    [Fact]
    public void Rank_PrefersHigherResolution()
    {
        var ranked = StreamResolver.Rank(new[]
        {
            Stream("Dune 1080p"),
            Stream("Dune 2160p HDR"),
            Stream("Dune 720p")
        });
        Assert.Equal("Dune 2160p HDR", ranked[0].Title);
        Assert.Equal("Dune 1080p", ranked[1].Title);
        Assert.Equal("Dune 720p", ranked[2].Title);
    }

    [Fact]
    public void Rank_PrefersHdrOverSdr_AtSameResolution()
    {
        var ranked = StreamResolver.Rank(new[]
        {
            Stream("Dune 2160p SDR"),
            Stream("Dune 2160p DV")
        });
        Assert.Equal("Dune 2160p DV", ranked[0].Title);
    }

    [Fact]
    public void Rank_PrefersLargerFile_AtSameResolutionAndHdr()
    {
        var ranked = StreamResolver.Rank(new[]
        {
            Stream("Dune 1080p", size: 2L * 1024 * 1024 * 1024),
            Stream("Dune 1080p", size: 5L * 1024 * 1024 * 1024)
        });
        Assert.Equal(5L * 1024 * 1024 * 1024, ranked[0].BehaviorHints!.VideoSize);
    }

    [Fact]
    public void Select_WithAuto_PicksBest()
    {
        var streams = new[]
        {
            Stream("Dune 1080p"),
            Stream("Dune 2160p HDR")
        };
        Assert.Equal("Dune 2160p HDR", StreamResolver.Select(streams, null)!.Title);
        Assert.Equal("Dune 2160p HDR", StreamResolver.Select(streams, "auto")!.Title);
    }

    [Fact]
    public void Select_WithQuality_MatchingStreamFirst()
    {
        var streams = new[]
        {
            Stream("Dune 2160p HDR"),
            Stream("Dune 1080p WEB-DL")
        };
        Assert.Equal("Dune 1080p WEB-DL", StreamResolver.Select(streams, "1080p")!.Title);
    }

    [Fact]
    public void Select_WithQualityNoMatch_FallsBackToBest()
    {
        var streams = new[]
        {
            Stream("Dune 1080p"),
            Stream("Dune 720p")
        };
        Assert.Equal("Dune 1080p", StreamResolver.Select(streams, "2160p")!.Title);
    }

    [Fact]
    public void Select_Empty_ReturnsNull()
    {
        Assert.Null(StreamResolver.Select(Array.Empty<StreamResult>(), null));
    }
}
