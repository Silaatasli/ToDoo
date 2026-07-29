using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;


/// RabbitMQ kuyrugundan bildirim mesajlarini alir, Redis'e yazar ve SignalR ile iletir.

public class NotificationConsumerHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<NotificationConsumerHostedService> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public NotificationConsumerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<NotificationConsumerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer baglantisi koptu; 5 sn sonra yeniden denenecek.");
                await DisposeConnectionAsync();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        await DisposeConnectionAsync();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } && _connection is { IsOpen: true })
        {
            return;
        }

        await DisposeConnectionAsync();

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: _options.NotificationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await _channel.BasicQosAsync(0, 10, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: _options.NotificationQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "RabbitMQ bildirim consumer basladi. Queue={Queue}",
            _options.NotificationQueue);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            var message = JsonSerializer.Deserialize<NotificationMessage>(json, JsonOptions);
            if (message is null || message.TargetUserId <= 0)
            {
                await _channel.BasicAckAsync(args.DeliveryTag, false);
                return;
            }

            // Dispatch zaten Redis + SignalR yaptıysa tekrar yazma (cift bildirim olmasin).
            if (message.DirectDelivered)
            {
                await _channel.BasicAckAsync(args.DeliveryTag, false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
            var realtime = scope.ServiceProvider.GetRequiredService<IRealtimeNotificationSender>();

            await store.AddAsync(message);
            var unread = await store.GetUnreadCountAsync(message.TargetUserId);

            var dto = new NotificationItemDto
            {
                Id = message.Id,
                Type = message.Type,
                Title = message.Title,
                Body = message.Body,
                TeamId = message.TeamId,
                BoardId = message.BoardId,
                TaskId = message.TaskId,
                AnnouncementId = message.AnnouncementId,
                SprintId = message.SprintId,
                IsRead = false,
                CreatedAtUtc = message.CreatedAtUtc
            };

            await realtime.SendToUserAsync(message.TargetUserId, dto, unread);
            await _channel.BasicAckAsync(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bildirim mesajı işlenemedi.");
            try
            {
                await _channel.BasicNackAsync(args.DeliveryTag, false, requeue: true);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "RabbitMQ NACK başarısız.");
            }
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
                await _channel.DisposeAsync();
            }
            catch
            {
                // ignore
            }

            _channel = null;
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }
            catch
            {
                // ignore
            }

            _connection = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeConnectionAsync();
        await base.StopAsync(cancellationToken);
    }
}
