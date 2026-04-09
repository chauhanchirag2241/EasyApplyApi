using EasyApplyAPI.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Hangfire;

namespace EasyApplyAPI.Jobs
{
    public class CampaignDailyProcessor
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CampaignDailyProcessor> _logger;

        public CampaignDailyProcessor(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CampaignDailyProcessor> logger)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task ProcessDaily(string campaignId)
        {
            var dbName = _configuration["CosmosDb:DatabaseName"];
            var containerName = _configuration["CosmosDb:ContainerName"];
            var container = _cosmosClient.GetContainer(dbName, containerName);

            try
            {
                // Fetch the campaign to make sure it exists
                var campaignResp = await container.ReadItemAsync<Campaign>(campaignId, new PartitionKey(campaignId));
                var campaign = campaignResp.Resource;

                if (campaign.ScheduleType != "Daily")
                {
                    _logger.LogWarning($"Campaign {campaignId} is not set to Daily. Skipping recurring execution.");
                    return;
                }

                // Query all Pending prospects for this campaign
                var query = container.GetItemLinqQueryable<Prospect>(true)
                    .Where(p => p.Type == "Prospect" && p.CampaignId == campaignId && p.Status == "Pending")
                    .ToFeedIterator();

                int queuedCount = 0;
                while (query.HasMoreResults)
                {
                    foreach (var prospect in await query.ReadNextAsync())
                    {
                        prospect.HangfireJobId = BackgroundJob.Enqueue<SendEmailJob>(x => x.ProcessCampaignEmail(campaign.Id, prospect.Id));
                        await container.ReplaceItemAsync(prospect, prospect.Id, new PartitionKey(prospect.Id));
                        queuedCount++;
                    }
                }

                _logger.LogInformation($"Successfully enqueued {queuedCount} pending prospects for daily campaign {campaignId}");
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning($"Campaign {campaignId} not found during daily processing. Removing recurring job.");
                RecurringJob.RemoveIfExists(campaignId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process daily campaign {campaignId}");
                throw;
            }
        }
    }
}
