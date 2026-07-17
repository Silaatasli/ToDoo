namespace Todoo.Business.Abstract;

public interface IJwtTokenService
{
    /// <summary>
    /// Verilen kullanici icin JWT access token uretir. <paramref name="jti"/>, token'in Redis
    /// allowlist'inde takip edilebilmesi icin "jti" claim'i olarak token'a eklenir.
    /// </summary>
    string CreateToken(int userId, string jti);
}
