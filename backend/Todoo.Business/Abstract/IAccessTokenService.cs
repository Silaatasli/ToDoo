namespace Todoo.Business.Abstract;

/// <summary>
/// Access token'lari (JWT) Redis uzerinde bir "allowlist" olarak yonetir.
/// Her access token'in benzersiz bir "jti" (JWT ID) degeri vardir; token uretilirken
/// bu jti Redis'e yazilir, her istekte aktif mi diye kontrol edilir. Boylece logout,
/// sifre sifirlama gibi durumlarda access token'lar suresi dolmadan aninda iptal edilebilir.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// Yeni bir access token oturumu icin benzersiz jti uretir, Redis'e (TTL = access token omru)
    /// kaydeder ve dondurur. Uretilen jti JWT icine "jti" claim'i olarak konur.
    /// </summary>
    Task<string> IssueAsync(int userId);

    /// <summary>
    /// Verilen jti'nin Redis'te hala aktif (iptal edilmemis ve suresi dolmamis) olup olmadigini kontrol eder.
    /// </summary>
    Task<bool> IsActiveAsync(string jti);

    /// <summary>
    /// Tek bir access token'i (jti) iptal eder. Logout icin kullanilir.
    /// </summary>
    Task RevokeAsync(string jti);

    /// <summary>
    /// Kullanicinin tum aktif access token'larini iptal eder. "Tum cihazlardan cikis" ve
    /// sifre sifirlama sonrasi kullanilir.
    /// </summary>
    Task RevokeAllForUserAsync(int userId);
}
