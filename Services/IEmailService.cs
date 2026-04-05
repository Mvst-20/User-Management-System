namespace UserManagementSystem.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
    Task SendVerificationEmailAsync(string to, string username, string token, bool isEmailChange = false, string? newEmail = null);
}
