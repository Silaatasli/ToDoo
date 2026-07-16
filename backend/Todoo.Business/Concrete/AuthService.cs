using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.Business.Options;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;

namespace Todoo.Business.Concrete;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordService _passwordService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITeamService _teamService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IPasswordResetTokenService _passwordResetTokenService;
    private readonly IEmailService _emailService;
    private readonly PasswordResetOptions _passwordResetOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordService passwordService,
        IJwtTokenService jwtTokenService,
        ITeamService teamService,
        IRefreshTokenService refreshTokenService,
        IPasswordResetTokenService passwordResetTokenService,
        IEmailService emailService,
        IOptions<PasswordResetOptions> passwordResetOptions,
        ILogger<AuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordService = passwordService;
        _jwtTokenService = jwtTokenService;
        _teamService = teamService;
        _refreshTokenService = refreshTokenService;
        _passwordResetTokenService = passwordResetTokenService;
        _emailService = emailService;
        _passwordResetOptions = passwordResetOptions.Value;
        _logger = logger;
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
                Message = "Bu e-posta adresi zaten kayıtlı."
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
            Message = "Kayıt işlemi başarılı.",
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Token = _jwtTokenService.CreateToken(user.Id, user.Email),
            RefreshToken = await _refreshTokenService.IssueAsync(user.Id, user.Email)
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
                Message = "E-posta veya şifre hatalı."
            };
        }

        return new AuthResultDto
        {
            Success = true,
            Message = "Giriş işlemi başarılı.",
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Token = _jwtTokenService.CreateToken(user.Id, user.Email),
            RefreshToken = await _refreshTokenService.IssueAsync(user.Id, user.Email)
        };
    }

    public async Task<AuthResultDto> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Refresh token gerekli."
            };
        }

        var rotation = await _refreshTokenService.ValidateAndRotateAsync(refreshToken);
        if (rotation is null)
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Oturum süresi dolmuş veya geçersiz. Lütfen tekrar giriş yapın."
            };
        }

        return new AuthResultDto
        {
            Success = true,
            Message = "Token yenilendi.",
            UserId = rotation.UserId,
            Email = rotation.Email,
            Token = _jwtTokenService.CreateToken(rotation.UserId, rotation.Email),
            RefreshToken = rotation.NewRefreshToken
        };
    }

    public async Task LogoutAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        await _refreshTokenService.RevokeAsync(refreshToken);
    }

    public async Task LogoutAllAsync(int userId)
    {
        await _refreshTokenService.RevokeAllForUserAsync(userId);
    }

    public async Task<AuthResultDto> ForgotPasswordAsync(string email)
    {
        var genericMessage = "Şifre sıfırlama linki gönderildi.";
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var users = await _unitOfWork.Users.GetAllAsync();
        var user = users.FirstOrDefault(existingUser => existingUser.Email == normalizedEmail);

        if (user is null)
        {
            return new AuthResultDto
            {
                Success = true,
                Message = genericMessage
            };
        }

        var token = await _passwordResetTokenService.IssueAsync(user.Id, user.Email);
        var resetUrl =
            $"{_passwordResetOptions.FrontendResetUrl.TrimEnd('/')}?token={WebUtility.UrlEncode(token)}";

        var htmlBody = $"""
            <p>Merhaba {WebUtility.HtmlEncode(user.FirstName)},</p>
            <p>ToDoo hesabın için şifre sıfırlama talebinde bulundun.</p>
            <p><a href="{resetUrl}">Şifreni sıfırlamak için buraya tıkla</a></p>
            <p>Bu link {_passwordResetOptions.TokenExpirationMinutes} dakika geçerlidir.</p>
            <p>Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin.</p>
            """;

        try
        {
            await _emailService.SendAsync(user.Email, "ToDoo - Şifre Sıfırlama", htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şifre sıfırlama e-postası gonderilemedi: {Email}", user.Email);
            return new AuthResultDto
            {
                Success = false,
                Message = "Şifre sıfırlama e-postası gonderilemedi. Lutfen daha sonra tekrar deneyin."
            };
        }

        return new AuthResultDto
        {
            Success = true,
            Message = genericMessage
        };
    }

    public async Task<AuthResultDto> ResetPasswordAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Geçersiz veya süresi dolmuş şifre sıfırlama linki."
            };
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Yeni şifre en az 6 karakter olmalıdır."
            };
        }

        var consumed = await _passwordResetTokenService.ConsumeAsync(token);
        if (consumed is null)
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Geçersiz veya süresi dolmuş şifre sıfırlama linki."
            };
        }

        var user = await _unitOfWork.Users.GetByIdAsync(consumed.Value.UserId);
        if (user is null)
        {
            return new AuthResultDto
            {
                Success = false,
                Message = "Kullanıcı bulunamadı."
            };
        }

        _passwordService.CreatePasswordHash(newPassword, out var passwordHash, out var passwordSalt);
        user.PasswordHash = passwordHash;
        user.PasswordSalt = passwordSalt;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        await _refreshTokenService.RevokeAllForUserAsync(user.Id);

        return new AuthResultDto
        {
            Success = true,
            Message = "Şifren guncellendi. Simdi yeni şifrenle giriş yapabilirsin."
        };
    }
}
