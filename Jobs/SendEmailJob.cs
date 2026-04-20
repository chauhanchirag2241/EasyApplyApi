using EasyApplyAPI.Models;
using EasyApplyAPI.Services;
using Microsoft.Azure.Cosmos;

namespace EasyApplyAPI.Jobs
{
    public class SendEmailJob
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly ILogger<SendEmailJob> _logger;

        public SendEmailJob(CosmosClient cosmosClient, IConfiguration configuration, IEmailService emailService, ILogger<SendEmailJob> logger)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task ProcessCampaignEmail(string campaignId, string prospectId)
        {
            var dbName = _configuration["CosmosDb:DatabaseName"];
            var containerName = _configuration["CosmosDb:ContainerName"];
            var container = _cosmosClient.GetContainer(dbName, containerName);

            try
            {
                // Fetch Campaign
                var campaignResp = await container.ReadItemAsync<Campaign>(campaignId, new PartitionKey(campaignId));
                var campaign = campaignResp.Resource;

                // Fetch Prospect
                var prospectResp = await container.ReadItemAsync<Prospect>(prospectId, new PartitionKey(prospectId));
                var prospect = prospectResp.Resource;

                if (prospect.Status == "Sent")
                    return; // Already sent

                // Determine IST Greeting
                var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                var istTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
                string greeting = "Hello";
                if (istTime.Hour < 12) greeting = "Good Morning";
                else if (istTime.Hour < 17) greeting = "Good Afternoon";
                else greeting = "Good Evening";

                // Parse Body
                string parsedBody = campaign.Body
                    .Replace("{{Greeting}}", greeting)
                    .Replace("{{Company}}", string.IsNullOrWhiteSpace(prospect.CompanyName) ? "" : prospect.CompanyName)
                    .Replace("{{Manager}}", string.IsNullOrWhiteSpace(prospect.ManagerName) ? "" : prospect.ManagerName);

                // Send Email
                await _emailService.SendEmailAsync(prospect.EmailAddress, campaign.Subject, parsedBody, campaign.ResumeFilePath);

                // Update Prospect
                prospect.Status = "Sent";
                prospect.SentDate = DateTime.UtcNow;

                await container.ReplaceItemAsync(prospect, prospectId, new PartitionKey(prospectId));

                _logger.LogInformation($"Successfully processed prospect {prospectId} for campaign {campaignId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process email for prospect {prospectId}");
                try 
                {
                    // Attempt to mark as Failed
                    var prospectResp = await container.ReadItemAsync<Prospect>(prospectId, new PartitionKey(prospectId));
                    var prospect = prospectResp.Resource;
                    prospect.Status = "Failed";
                    prospect.SentDate = DateTime.UtcNow;
                    await container.ReplaceItemAsync(prospect, prospectId, new PartitionKey(prospectId));
                }
                catch { } // ignore secondary failure
                
                throw; // rethrow for hangfire retry
            }
        }
    }
}
