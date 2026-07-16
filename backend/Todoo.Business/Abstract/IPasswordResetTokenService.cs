namespace Todoo.Business.Abstract;

public interface IPasswordResetTokenService
{
    Task<string> IssueAsync(int userId, string email);

    /// <summary>
    /// Token gecerliyse kullanici bilgisini dondurur ve token'i tek kullanimlik oldugu icin siler.
    /// </summary>
    Task<(int UserId, string Email)?> ConsumeAsync(string rawToken);
}
