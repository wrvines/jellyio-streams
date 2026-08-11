using Jellyfin.Plugin.AIOStreams.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AIOStreams;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        var version = typeof(PluginServiceRegistrator).Assembly.GetName().Version?.ToString(3) ?? "1.0";

        serviceCollection.AddHttpClient<AIOStreamsClient>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin.Plugin.AIOStreams/{version}");
        });

        serviceCollection.AddSingleton<CatalogSynchronizer>();
    }
}
