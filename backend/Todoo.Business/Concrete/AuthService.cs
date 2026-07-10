using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;

namespace Todoo.Business.Concrete;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordService _passwordService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITeamService _teamService;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordService passwordService,
        IJwtTokenService jwtTokenService,
        ITeamService teamService)
    {
        _unitOfWork = unitOfWork;
        _passwordService = passwordService;
        _jwtTokenService = jwtTokenService;
        _teamService = teamService;
    }

    public async Task<AuthResultDto> RegisterAsync(string firstName, string lastName, string email, string password)
    {
        var normalizedFirstName = firstName.Trim();
        var normalizedLastName = lastName.Trim();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedFirstName) || string.IsNullOrWhiteSpace(normalizedLastName))
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Ad ve soyad zorunludur."
            };
        }

        var users = await _unitOfWork.Users.GetAllAsync();
        var existingUser = users.FirstOrDefault(user => user.Email == normalizedEmail);
        if (existingUser is not null)
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Bu e-posta adresi zaten kayitli."
            };
        }

        _passwordService.CreatePasswordHash(password, out var passwordHash, out var passwordSalt);

        var user = new User
        {
            FirstName = normalizedFirstName,
            LastName = normalizedLastName,
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt
        };

        _unitOfWork.Users.Add(user);
        await _unitOfWork.SaveChangesAsync();

        await _teamService.EnsurePersonalTeamAsync(user.Id);

        return new AuthResultDto
        {
            Success = true,
            Message = "Kayit islemi basarili.",
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Token = _jwtTokenService.CreateToken(user.Id, user.Email)
        };
    }

    public async Task<AuthResultDto> LoginAsync(string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var users = await _unitOfWork.Users.GetAllAsync();
        var user = users.FirstOrDefault(existingUser => existingUser.Email == normalizedEmail);

        if (user is null || !_passwordService.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "E-posta veya sifre hatali."
            };
        }

        return new AuthResultDto
        {
            Success = true,
            Message = "Giris islemi basarili.",
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Token = _jwtTokenService.CreateToken(user.Id, user.Email)
        };
    }
}
