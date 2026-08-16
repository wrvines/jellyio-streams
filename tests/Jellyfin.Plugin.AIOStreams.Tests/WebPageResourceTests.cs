using Jellyfin.Plugin.AIOStreams.Services;
using Xunit;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public sealed class WebPageResourceTests
{
    [Fact]
    public async Task SearchPage_ContainsConnectionPanelAndSaveApi()
    {
        var assembly = typeof(StreamFolder).Assembly;
        await using var stream = assembly.GetManifestResourceStream(
            "Jellyfin.Plugin.AIOStreams.Web.searchPage.html");

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = await reader.ReadToEndAsync();

        Assert.Contains("fldAddonUrl", html);
        Assert.Contains("btnSaveAddonUrl", html);
        Assert.Contains("updatePluginConfiguration", html);
        Assert.Contains("PlaybackSecret", html);
    }
}
