using Todoo.Business.Models;

namespace Todoo.Business.Abstract;

public interface IRefreshTokenService
{
    /// <summary>
    /// Kullanici icin yeni bir refresh token uretir, Redis'e (session olarak) kaydeder ve dondurur.
    /// </summary>
    Task<string> IssueAsync(int userId, string email);

    /// <summary>
    /// Verilen refresh token'i dogrular; gecerliyse eskisini iptal edip yenisini uretir (rotation).
    /// Token gecersiz/suresi dolmus ise null doner.
    /// </summary>
    Task<RefreshTokenRotationResult?> ValidateAndRotateAsync(string refreshToken);

    /// <summary>
    /// Tek bir refresh token'i (tek bir oturumu) iptal eder. Logout icin kullanilir.
    /// </summary>
    Task RevokeAsync(string refreshToken);

    /// <summary>
    /// Kullanicinin tum aktif oturumlarini/refresh token'larini iptal eder. "Tum cihazlardan cikis yap" icin kullanilir.
    /// </summary>
    Task RevokeAllForUserAsync(int userId);
}
