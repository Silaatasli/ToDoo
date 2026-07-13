using System.Globalization;
using System.Text.RegularExpressions;
using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;

namespace Todoo.Business.Concrete;

public class UserService : IUserService
{
    private const long MaxPhotoSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;
    private readonly ILuceneSearchIndex _searchIndex;

    public UserService(IUnitOfWork unitOfWork, IFileStorageService fileStorage, ILuceneSearchIndex searchIndex)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _searchIndex = searchIndex;
    }

    public async Task<ServiceResult<UserProfileDto>> GetOwnProfileAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: true));
    }

    public async Task<ServiceResult<UserProfileDto>> GetProfileAsync(int targetUserId, int requesterUserId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(targetUserId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (targetUserId != requesterUserId && !await SharesTeamAsync(targetUserId, requesterUserId))
        {
            return ServiceResult<UserProfileDto>.Fail("Bu kullanicinin profilini goruntuleme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: targetUserId == requesterUserId));
    }

    public async Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(int userId, string? firstName, string? lastName, string? phoneNumber, string? title)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        user.FirstName = Normalize(firstName);
        user.LastName = Normalize(lastName);
        user.PhoneNumber = Normalize(phoneNumber);
        user.Title = Normalize(title);

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();
        await IndexPersonDocumentAsync(user);

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: true));
    }

    public async Task<ServiceResult<IEnumerable<UserSearchResultDto>>> SearchUsersAsync(string query, int requesterUserId)
    {
        var term = query?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length < 3)
        {
            return ServiceResult<IEnumerable<UserSearchResultDto>>.Fail("En az 3 karakter girin.");
        }

        var users = (await _unitOfWork.Users.GetAllAsync())
            .Where(user => MatchesSearch(user, term))
            .OrderBy(user => UserDisplayNameHelper.Format(user), StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: true))
            .Take(15)
            .Select(user => new UserSearchResultDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = UserDisplayNameHelper.Format(user),
                HasProfilePhoto = !string.IsNullOrWhiteSpace(user.ProfilePhotoObjectKey)
            })
            .ToList();

        return ServiceResult<IEnumerable<UserSearchResultDto>>.Ok(users);
    }

    public async Task<ServiceResult<UserProfileDto>> UploadProfilePhotoAsync(
        int userId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream fileStream)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (sizeBytes <= 0)
        {
            return ServiceResult<UserProfileDto>.Fail("Bos dosya yuklenemez.");
        }

        if (sizeBytes > MaxPhotoSizeBytes)
        {
            return ServiceResult<UserProfileDto>.Fail("Profil fotografi en fazla 5 MB olabilir.");
        }

        var normalizedContentType = ResolveContentType(contentType, fileName);
        if (!AllowedContentTypes.Contains(normalizedContentType))
        {
            return ServiceResult<UserProfileDto>.Fail("Desteklenmeyen dosya tipi. JPG, PNG, WEBP veya GIF yukleyin.");
        }

        var safeFileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return ServiceResult<UserProfileDto>.Fail("Gecersiz dosya adi.");
        }

        var objectKey = $"users/{user.Id}/avatar/{Guid.NewGuid():N}-{safeFileName}";
        var previousKey = user.ProfilePhotoObjectKey;

        try
        {
            await _fileStorage.UploadAsync(objectKey, fileStream, sizeBytes, normalizedContentType);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<UserProfileDto>.Fail(ex.Message, ServiceErrorKind.Validation);
        }

        user.ProfilePhotoObjectKey = objectKey;
        user.ProfilePhotoContentType = normalizedContentType;
        user.ProfilePhotoFileName = safeFileName;
        user.ProfilePhotoSizeBytes = sizeBytes;

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();
        await IndexPersonDocumentAsync(user);

        if (!string.IsNullOrWhiteSpace(previousKey) &&
            !string.Equals(previousKey, objectKey, StringComparison.Ordinal))
        {
            try
            {
                await _fileStorage.DeleteAsync(previousKey);
            }
            catch
            {
                // Eski dosya silinemese de yeni foto kaydi gecerli kalsin.
            }
        }

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: true));
    }

    public async Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> DownloadProfilePhotoAsync(
        int targetUserId,
        int requesterUserId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(targetUserId);
        if (user is null)
        {
            return ServiceResult<(Stream, string, string)>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (targetUserId != requesterUserId && !await SharesTeamAsync(targetUserId, requesterUserId))
        {
            return ServiceResult<(Stream, string, string)>.Fail(
                "Bu kullanicinin profilini goruntuleme yetkiniz yok.",
                ServiceErrorKind.Forbidden);
        }

        if (string.IsNullOrWhiteSpace(user.ProfilePhotoObjectKey))
        {
            return ServiceResult<(Stream, string, string)>.Fail("Profil fotografi bulunamadi.", ServiceErrorKind.NotFound);
        }

        var stream = await _fileStorage.DownloadAsync(user.ProfilePhotoObjectKey);
        var contentType = user.ProfilePhotoContentType ?? "application/octet-stream";
        var fileName = user.ProfilePhotoFileName ?? "avatar";
        return ServiceResult<(Stream, string, string)>.Ok((stream, contentType, fileName));
    }

    public async Task<ServiceResult<UserProfileDto>> DeleteProfilePhotoAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        var previousKey = user.ProfilePhotoObjectKey;
        user.ProfilePhotoObjectKey = null;
        user.ProfilePhotoContentType = null;
        user.ProfilePhotoFileName = null;
        user.ProfilePhotoSizeBytes = null;

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();
        await IndexPersonDocumentAsync(user);

        if (!string.IsNullOrWhiteSpace(previousKey))
        {
            try
            {
                await _fileStorage.DeleteAsync(previousKey);
            }
            catch
            {
                // DB kaydi temizlendigi icin MinIO silme hatasi engelleyici olmasin.
            }
        }

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: true));
    }

    private async Task IndexPersonDocumentAsync(User user)
    {
        var membershipTeamIds = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.UserId == user.Id)
            .Select(member => member.TeamId)
            .ToHashSet();

        var nonPersonalTeamIds = (await _unitOfWork.Teams.GetAllAsync())
            .Where(team => membershipTeamIds.Contains(team.Id) && !team.IsPersonal)
            .Select(team => team.Id)
            .ToList();

        _searchIndex.IndexPerson(user, nonPersonalTeamIds);
    }

    private async Task<bool> SharesTeamAsync(int targetUserId, int requesterUserId)
    {
        var memberships = await _unitOfWork.TeamMembers.GetAllAsync();
        var requesterTeams = memberships
            .Where(member => member.UserId == requesterUserId)
            .Select(member => member.TeamId)
            .ToHashSet();

        return memberships.Any(member => member.UserId == targetUserId && requesterTeams.Contains(member.TeamId));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool MatchesSearch(User user, string term)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var firstName = user.FirstName ?? string.Empty;
        var lastName = user.LastName ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();

        return firstName.Contains(term, comparison)
            || lastName.Contains(term, comparison)
            || fullName.Contains(term, comparison);
    }

    private static UserProfileDto MapToDto(User user, bool isSelf) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
        Title = user.Title,
        CreatedDate = user.CreatedDate,
        IsSelf = isSelf,
        HasProfilePhoto = !string.IsNullOrWhiteSpace(user.ProfilePhotoObjectKey)
    };

    private static string ResolveContentType(string contentType, string fileName)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim();

        if (string.Equals(normalized, "image/jpg", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "image/jpeg";
        }

        if (AllowedContentTypes.Contains(normalized))
        {
            return normalized;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => normalized
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        trimmed = Regex.Replace(trimmed, @"[^\w\.\-]", "_");
        return trimmed.Length > 180 ? trimmed[..180] : trimmed;
    }
}
