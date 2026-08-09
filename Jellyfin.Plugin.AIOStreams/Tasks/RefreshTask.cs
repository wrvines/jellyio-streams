using Jellyfin.Plugin.AIOStreams.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Tasks;

/// <summary>
/// Scheduled task that refreshes the AIOStreams library.
/// </summary>
public sealed class RefreshTask : IScheduledTask
{
    private readonly CatalogSynchronizer _synchronizer;
    private readonly ILogger<RefreshTask> _logger;

    public RefreshTask(CatalogSynchronizer synchronizer, ILogger<RefreshTask> logger)
    {
        _synchronizer = synchronizer;
        _logger = logger;
    }

    public string Name => "Refresh AIOStreams library";

    public string Key => "AIOStreamsRefresh";

    public string Description => "Rebuilds the Jellyfin library from the configured AIOStreams catalogs.";

    public string Category => "Library";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _synchronizer.SyncCatalogsAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = Plugin.Instance?.Configuration.RefreshIntervalHours ?? 0;
        return BuildTriggers(hours);
    }

    /// <summary>
    /// Builds the default triggers for a given refresh interval in hours (0 = no triggers).
    /// </summary>
    public static IEnumerable<TaskTriggerInfo> BuildTriggers(int intervalHours)
    {
        if (intervalHours <= 0)
        {
            yield break;
        }

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
        };
    }
}
