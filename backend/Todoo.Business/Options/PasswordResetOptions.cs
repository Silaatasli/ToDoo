namespace Todoo.Business.Options;

public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// Frontend reset sayfasinin base URL'i. Ornek: http://localhost:4200/reset-password
    /// </summary>
    public string FrontendResetUrl { get; set; } = "http://localhost:4200/reset-password";

    public int TokenExpirationMinutes { get; set; } = 30;
}
