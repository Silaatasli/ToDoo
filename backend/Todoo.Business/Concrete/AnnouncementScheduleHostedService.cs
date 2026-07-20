using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;

namespace Todoo.Business.Concrete;

/// <summary>
/// Zamanlanmis duyurulari periyodik kontrol edip yayinlar ve bildirim gonderir.
/// </summary>
public class AnnouncementScheduleHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var announcementService = scope.ServiceProvider.GetRequiredService<ITeamAnnouncementService>();
                var publishedCount = await announcementService.PublishDueScheduledAsync(stoppingToken);
                if (publishedCount > 0)
                {
                    _logger.LogInformation("{Count} zamanlanmis duyuru yayinlandi.", publishedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zamanlanmis duyuru yayini kontrolu basarisiz.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
