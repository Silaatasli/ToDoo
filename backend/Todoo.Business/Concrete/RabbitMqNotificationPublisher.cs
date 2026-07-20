using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;

public class RabbitMqNotificationPublisher : INotificationPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqNotificationPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqNotificationPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqNotificationPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (message.TargetUserId <= 0 || string.IsNullOrWhiteSpace(message.Type))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureChannelAsync(cancellationToken);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));
            var props = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = message.Id
            };

            await _channel!.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.NotificationQueue,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ bildirim publish basarisiz. Type={Type}, UserId={UserId}", message.Type, message.TargetUserId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } && _connection is { IsOpen: true })
        {
            return;
        }

        await DisposeChannelAsync();

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password
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
    }

    private async Task DisposeChannelAsync()
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
                // ignore dispose errors
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
                // ignore dispose errors
            }

            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DisposeChannelAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
