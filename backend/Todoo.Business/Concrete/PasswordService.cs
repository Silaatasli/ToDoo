using System.Security.Cryptography;
using Todoo.Business.Abstract;

namespace Todoo.Business.Concrete;

public class PasswordService : IPasswordService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100000;

    public void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
    {
        passwordSalt = RandomNumberGenerator.GetBytes(SaltSize);
        passwordHash = Rfc2898DeriveBytes.Pbkdf2(password, passwordSalt, Iterations, HashAlgorithmName.SHA512, HashSize);
    }

    public bool VerifyPassword(string password, byte[] passwordHash, byte[] passwordSalt)
    {
        var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, passwordSalt, Iterations, HashAlgorithmName.SHA512, HashSize);
        return CryptographicOperations.FixedTimeEquals(computedHash, passwordHash);
    }
}
