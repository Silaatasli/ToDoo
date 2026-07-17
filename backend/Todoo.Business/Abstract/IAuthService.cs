using Todoo.Business.Models;

namespace Todoo.Business.Abstract;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(string firstName, string lastName, string email, string password);
    Task<AuthResultDto> LoginAsync(string email, string password);
    Task<AuthResultDto> RefreshAsync(string refreshToken);
    Task LogoutAsync(string refreshToken, string? accessTokenJti);
    Task LogoutAllAsync(int userId);
    Task<AuthResultDto> ForgotPasswordAsync(string email);
    Task<AuthResultDto> ResetPasswordAsync(string token, string newPassword);
}
