using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;

namespace Todoo.Business.Concrete;

/// <summary>
/// Zamanlanmis duyurulari kontrol edip yayinlar.
/// Vade yakinsa tam saatte uyanir; yeni zamanlamalar icin en fazla 1 sn'de bir tarar.
/// </summary>
public class AnnouncementScheduleHostedService : BackgroundService
{
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxWaitForNext = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DueRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnnouncementScheduleHostedService> _logger;

    public AnnouncementScheduleHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnnouncementScheduleHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Uygulama ayaga kalkinca hemen bir tur bak.
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdlePollInterval;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var announcementService = scope.ServiceProvider.GetRequiredService<ITeamAnnouncementService>();
                var publishedCount = await announcementService.PublishDueScheduledAsync(stoppingToken);
                if (publishedCount > 0)
                {
                    _logger.LogInformation("{Count} zamanlanmis duyuru yayinlandi.", publishedCount);
                }

                delay = await ResolveDelayAsync(announcementService, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zamanlanmis duyuru yayini kontrolu basarisiz.");
                delay = IdlePollInterval;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<TimeSpan> ResolveDelayAsync( // Zamanlanmis duyurulari yayinlamak icin ne kadar bekleyecegimizi hesapla.
        ITeamAnnouncementService announcementService,
        CancellationToken stoppingToken)
    {
        var nextAt = await announcementService.GetNextScheduledPublishAtUtcAsync(stoppingToken);
        if (!nextAt.HasValue)
        {
            return IdlePollInterval;
        }

        var until = nextAt.Value - DateTime.UtcNow;
        if (until <= TimeSpan.Zero)
        {
            return DueRetryDelay;
        }

        return until < MaxWaitForNext ? until : MaxWaitForNext;
    }
}
