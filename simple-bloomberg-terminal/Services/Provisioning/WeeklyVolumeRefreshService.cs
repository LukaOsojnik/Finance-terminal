using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Provisioning;

/// <summary>
/// Weekly background top-up of stored trading-volume series. Yahoo buckets volume into weekly bars, so a
/// weekly cadence matches the data's resolution — a daily poll would just re-fetch the same in-progress
/// week. Each tick selects active companies that ALREADY have volume rows whose newest stored week is 7+
/// days old, then appends only the weeks Yahoo has that we don't (existing bars are skipped, never
/// rewritten). A BackgroundService is a singleton, so it opens a DI scope per tick to use the scoped
/// repository / provisioning service — resolving a scoped service straight into a singleton throws at
/// startup (the ASP.NET Core equivalent of the gotcha you'd hit injecting a request-scoped bean into a
/// Spring singleton).
/// </summary>
public class WeeklyVolumeRefreshService(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklyVolumeRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup, then every 7 days. The "7+ days stale" filter makes a startup pass a no-op
        // when the data is already current, so frequent restarts can't trigger redundant Yahoo traffic.
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RefreshAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Weekly volume refresh failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);

        using var scope = scopeFactory.CreateScope();
        var companies = scope.ServiceProvider.GetRequiredService<ICompanyRepository>();
        var provisioning = scope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();

        var ids = companies.CompanyIdsWithStaleVolume(cutoff);
        if (ids.Count == 0) return;

        logger.LogInformation("Weekly volume refresh: {Count} companies with stale volume", ids.Count);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            // Best-effort per company: a single ticker's Yahoo hiccup shouldn't abort the whole run.
            try
            {
                var result = await provisioning.AppendNewWeeklyVolumeAsync(id);
                if (result is { Status: VolumeIngestStatus.Ok, RowCount: > 0 })
                    logger.LogInformation("Company {Id}: appended {Rows} new weekly bars", id, result.RowCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Weekly volume refresh failed for company {Id}", id);
            }
        }
    }
}
