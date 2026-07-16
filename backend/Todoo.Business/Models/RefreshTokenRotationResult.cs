namespace Todoo.Business.Models;

public class RefreshTokenRotationResult
{
    public int UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NewRefreshToken { get; set; } = string.Empty;
}
