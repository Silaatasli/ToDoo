namespace Todoo.Business.Abstract;

public interface IJwtTokenService
{
    string CreateToken(int userId, string email);
}
