using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Caching.Memory;
using System.IO;

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
        private readonly IMemoryCache _cache;
        private readonly IBlobStorageService _blobService;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IMemoryCache cache, IBlobStorageService blobService)
        {
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _blobService = blobService;
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
            if (!string.IsNullOrEmpty(attachmentPath))
            {
                if (attachmentPath.StartsWith("http"))
                {
                    // Handle Azure Blob Storage URL with Caching
                    var cacheKey = $"resume_{attachmentPath}";
                    if (!_cache.TryGetValue(cacheKey, out (byte[] content, string contentType) fileData))
                    {
                        _logger.LogInformation("Downloading resume from Azure for the first time: {Url}", attachmentPath);
                        fileData = await _blobService.DownloadFileAsync(attachmentPath);
                        
                        // Cache for 10 minutes
                        _cache.Set(cacheKey, fileData, TimeSpan.FromMinutes(10));
                    }

                    builder.Attachments.Add(Path.GetFileName(attachmentPath), fileData.content, ContentType.Parse(fileData.contentType));
                    
                    // Clean up filename for the attachment if it has GUID
                    var lastAttachment = builder.Attachments.Last();
                    var fileName = lastAttachment.ContentType.Name;
                    if (fileName.Length > 36 && fileName[36] == '_')
                    {
                        var cleanName = fileName.Substring(37);
                        lastAttachment.ContentType.Name = cleanName;
                        lastAttachment.ContentDisposition.FileName = cleanName;
                    }
                }
                else if (File.Exists(attachmentPath))
                {
                    // Handle local file path (fallback/backward compatibility)
                    var attachment = await builder.Attachments.AddAsync(attachmentPath);
                    var fileName = Path.GetFileName(attachmentPath);
                    if (fileName.Length > 36 && fileName[36] == '_')
                    {
                        attachment.ContentDisposition.FileName = fileName.Substring(37);
                        attachment.ContentType.Name = fileName.Substring(37);
                    }
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
