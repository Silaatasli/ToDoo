namespace Todoo.Business.Abstract;

public interface IEmailService
{
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
