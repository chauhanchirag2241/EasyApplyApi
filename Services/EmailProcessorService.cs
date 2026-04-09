using EasyApplyAPI.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace EasyApplyAPI.Services
{
    public interface IEmailProcessorService
    {
        Task<int> ProcessAllPendingEmailsAsync();
    }

    public class EmailProcessorService : IEmailProcessorService
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly ILogger<EmailProcessorService> _logger;

        public EmailProcessorService(
            CosmosClient cosmosClient, 
            IConfiguration configuration, 
            IEmailService emailService, 
            ILogger<EmailProcessorService> logger)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<int> ProcessAllPendingEmailsAsync()
        {
            var dbName = _configuration["CosmosDb:DatabaseName"];
            var containerName = _configuration["CosmosDb:ContainerName"];
            var container = _cosmosClient.GetContainer(dbName, containerName);

            int processedCount = 0;

            try
            {
                // 1. Get all Campaigns
                var campaignsQuery = container.GetItemLinqQueryable<Campaign>(true)
                    .Where(c => c.Type == "Campaign")
                    .ToFeedIterator();

                while (campaignsQuery.HasMoreResults)
                {
                    foreach (var campaign in await campaignsQuery.ReadNextAsync())
                    {
                        // 2. For each campaign, get all Pending prospects
                        var prospectsQuery = container.GetItemLinqQueryable<Prospect>(true)
                            .Where(p => p.Type == "Prospect" && p.CampaignId == campaign.Id && p.Status == "Pending")
                            .ToFeedIterator();

                        while (prospectsQuery.HasMoreResults)
                        {
                            foreach (var prospect in await prospectsQuery.ReadNextAsync())
                            {
                                try
                                {
                                    await ProcessSingleEmailAsync(container, campaign, prospect);
                                    processedCount++;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error processing email for prospect {prospect.Id} in campaign {campaign.Id}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during bulk email processing");
            }

            return processedCount;
        }

        private async Task ProcessSingleEmailAsync(Container container, Campaign campaign, Prospect prospect)
        {
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
                .Replace("{{Company}}", string.IsNullOrWhiteSpace(prospect.CompanyName) ? "your esteemed organization" : prospect.CompanyName)
                .Replace("{{Manager}}", string.IsNullOrWhiteSpace(prospect.ManagerName) ? "Hiring Manager" : prospect.ManagerName);

            // Send Email
            await _emailService.SendEmailAsync(prospect.EmailAddress, campaign.Subject, parsedBody, campaign.ResumeFilePath);

            // Update Prospect
            prospect.Status = "Sent";
            prospect.SentDate = DateTime.UtcNow;

            await container.ReplaceItemAsync(prospect, prospect.Id, new PartitionKey(prospect.Id));
        }
    }
}
