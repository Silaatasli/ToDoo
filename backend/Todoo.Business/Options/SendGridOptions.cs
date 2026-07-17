namespace Todoo.Business.Options;

public class SendGridOptions
{
    public const string SectionName = "SendGrid";

    public string ApiKey { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = "ToDoo";

    public string ApiBaseUrl { get; set; } = "https://api.sendgrid.com/";
}
