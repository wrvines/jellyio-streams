using Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class PlaybackTokenServiceTests
{
    private static PlaybackTokenService NewService()
        => new(PlaybackTokenService.GenerateSecret(), TimeSpan.FromDays(7));

    [Fact]
    public void IssueToken_ThenVerify_RoundTrips()
    {
        var service = NewService();
        var token = service.IssueToken("movie", "tt1234567", "auto");
        Assert.True(service.TryVerify(token, out var payload));
        Assert.NotNull(payload);
        Assert.Equal("movie", payload!.Type);
        Assert.Equal("tt1234567", payload.Id);
        Assert.Equal("auto", payload.Quality);
    }

    [Fact]
    public void IssueToken_WithQuality_RoundTrips()
    {
        var service = NewService();
        var token = service.IssueToken("series", "tt123:1:2", "1080p");
        Assert.True(service.TryVerify(token, out var payload));
        Assert.Equal("1080p", payload!.Quality);
    }

    [Fact]
    public void Verify_TamperedToken_ReturnsFalse()
    {
        var service = NewService();
        var token = service.IssueToken("movie", "tt1234567", "auto");
        var tampered = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];
        Assert.False(service.TryVerify(tampered, out _));
    }

    [Fact]
    public void Verify_ExpiredToken_ReturnsFalse()
    {
        var service = new PlaybackTokenService(PlaybackTokenService.GenerateSecret(), TimeSpan.FromSeconds(-1));
        var token = service.IssueToken("movie", "tt1234567", "auto");
        Assert.False(service.TryVerify(token, out _));
    }

    [Fact]
    public void Verify_Garbage_ReturnsFalse()
    {
        var service = NewService();
        Assert.False(service.TryVerify("not-a-token", out _));
        Assert.False(service.TryVerify("", out _));
        Assert.False(service.TryVerify("a.b.c", out _));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var token = new PlaybackTokenService("secret-a").IssueToken("movie", "tt1", "auto");
        Assert.False(new PlaybackTokenService("secret-b").TryVerify(token, out _));
    }
}
