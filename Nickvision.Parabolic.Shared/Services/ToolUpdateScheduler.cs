using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

/// <summary>
/// Checks for yt-dlp / ffmpeg updates shortly after UniFetcher starts and then every 12 hours while it's running.
/// The check is skipped automatically when the internet is down; it simply tries again next time.
/// </summary>
public class ToolUpdateScheduler : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private readonly ILogger<ToolUpdateScheduler> _logger;
    private readonly IToolUpdateService _toolUpdateService;

    public ToolUpdateScheduler(ILogger<ToolUpdateScheduler> logger, IToolUpdateService toolUpdateService)
    {
        _logger = logger;
        _toolUpdateService = toolUpdateService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RunCheckAsync(stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // App is closing
        }
    }

    private async Task RunCheckAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await _toolUpdateService.CheckAsync(false, null, stoppingToken);
            _logger.LogInformation($"Scheduled yt-dlp/ffmpeg update check: {result.Outcome}");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError($"Scheduled update check failed: {e.Message}");
        }
    }
}
