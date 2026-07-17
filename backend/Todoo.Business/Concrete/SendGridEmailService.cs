using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Todoo.Business.Abstract;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;

/// <summary>
/// SendGrid HTTP API uzerinden e-posta gonderir (SMTP yerine).
/// </summary>
public class SendGridEmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly SendGridOptions _options;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        HttpClient httpClient,
        IOptions<SendGridOptions> options,
        ILogger<SendGridEmailService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException(
                "SendGrid ayarlari eksik. ApiKey ve FromEmail yapilandirilmali.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new SendGridMailRequest
        {
            Personalizations =
            [
                new SendGridPersonalization
                {
                    To = [new SendGridEmailAddress { Email = toEmail }]
                }
            ],
            From = new SendGridEmailAddress
            {
                Email = _options.FromEmail,
                Name = _options.FromName
            },
            Subject = subject,
            Content =
            [
                new SendGridContent
                {
                    Type = "text/html",
                    Value = htmlBody
                }
            ]
        });

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "SendGrid e-posta gonderimi basarisiz. Status={StatusCode}, Body={Body}",
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException(
                $"SendGrid e-posta gonderimi basarisiz oldu ({(int)response.StatusCode}).");
        }

        _logger.LogInformation("E-posta SendGrid ile gonderildi: {ToEmail}", toEmail);
    }

    private sealed class SendGridMailRequest
    {
        [JsonPropertyName("personalizations")]
        public List<SendGridPersonalization> Personalizations { get; set; } = [];

        [JsonPropertyName("from")]
        public SendGridEmailAddress From { get; set; } = new();

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public List<SendGridContent> Content { get; set; } = [];
    }

    private sealed class SendGridPersonalization
    {
        [JsonPropertyName("to")]
        public List<SendGridEmailAddress> To { get; set; } = [];
    }

    private sealed class SendGridEmailAddress
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }
    }

    private sealed class SendGridContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text/html";

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
