using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace EasyApplyAPI.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body, string? attachmentPath);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body, string? attachmentPath)
        {
            var senderEmail = _configuration["EmailSettings:SenderEmail"]!;
            var senderName = _configuration["EmailSettings:SenderName"]!;
            var password = _configuration["EmailSettings:Password"];

            var emailMessage = new MimeMessage();
            
            // From & To with proper display names
            emailMessage.From.Add(new MailboxAddress(senderName, senderEmail));
            emailMessage.To.Add(new MailboxAddress(toEmail.Split('@')[0], toEmail));
            emailMessage.Subject = subject;
            
            // Reply-To header - Gmail checks for this
            emailMessage.ReplyTo.Add(new MailboxAddress(senderName, senderEmail));
            
            // Set proper date
            emailMessage.Date = DateTimeOffset.Now;

            // Build HTML body - minimal, clean structure (no excessive styling)
            var formattedHtmlBody = body.Replace("\r\n", "<br>").Replace("\n", "<br>");
            
            var finalHtml = $@"<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""></head>
<body>
<div style=""font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.6;color:#222;"">
{formattedHtmlBody}
</div>
</body>
</html>";

            var builder = new BodyBuilder 
            { 
                TextBody = body,
                HtmlBody = finalHtml
            };

            // Attach file if exists
            if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
            {
                var attachment = await builder.Attachments.AddAsync(attachmentPath);
                
                // Strip GUID prefix from filename for clean display
                var fileName = Path.GetFileName(attachmentPath);
                if (fileName.Length > 36 && fileName[36] == '_')
                {
                    attachment.ContentDisposition.FileName = fileName.Substring(37);
                    attachment.ContentType.Name = fileName.Substring(37);
                }
            }

            emailMessage.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            try
            {
                // Gmail requires port 587 with STARTTLS explicitly
                await client.ConnectAsync(
                    _configuration["EmailSettings:SmtpServer"],
                    int.Parse(_configuration["EmailSettings:SmtpPort"]!),
                    SecureSocketOptions.StartTls);

                await client.AuthenticateAsync(senderEmail, password);

                await client.SendAsync(emailMessage);
                await client.DisconnectAsync(true);
                _logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {ToEmail}", toEmail);
                throw;
            }
        }
    }
}
