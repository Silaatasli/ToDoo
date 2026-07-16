namespace Todoo.Business.Options;

public class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimit";

    public int LoginPermitLimit { get; set; } = 5;

    public int LoginWindowSeconds { get; set; } = 60;

    public int RegisterPermitLimit { get; set; } = 3;

    public int RegisterWindowSeconds { get; set; } = 300;

    public int RefreshPermitLimit { get; set; } = 20;

    public int RefreshWindowSeconds { get; set; } = 60;

    public int ForgotPasswordPermitLimit { get; set; } = 3;

    public int ForgotPasswordWindowSeconds { get; set; } = 300;
}
