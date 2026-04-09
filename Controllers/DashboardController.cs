using EasyApplyAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace EasyApplyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;

        public DashboardController(CosmosClient cosmosClient, IConfiguration configuration)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
        }

        private Container GetContainer() => _cosmosClient.GetContainer(
            _configuration["CosmosDb:DatabaseName"],
            _configuration["CosmosDb:ContainerName"]);

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var container = GetContainer();
            
            var query = container.GetItemLinqQueryable<Prospect>(true)
                .Where(p => p.Type == "Prospect");

            var iterator = query.ToFeedIterator();
            var allProspects = new List<Prospect>();

            while (iterator.HasMoreResults)
            {
                foreach (var item in await iterator.ReadNextAsync())
                {
                    allProspects.Add(item);
                }
            }

            var campaignsQuery = container.GetItemLinqQueryable<Campaign>(true)
                .Where(c => c.Type == "Campaign")
                .ToFeedIterator();

            var allCampaigns = new List<Campaign>();
            while (campaignsQuery.HasMoreResults)
            {
                foreach (var item in await campaignsQuery.ReadNextAsync())
                    allCampaigns.Add(item);
            }

            DateTime? lastJobRun = allProspects.Where(p => p.Status == "Sent" && p.SentDate.HasValue)
                                               .OrderByDescending(p => p.SentDate)
                                               .Select(p => p.SentDate)
                                               .FirstOrDefault();

            DateTime? nextJobScheduled = allCampaigns.Where(c => c.ScheduledTime > DateTime.UtcNow)
                                                     .OrderBy(c => c.ScheduledTime)
                                                     .Select(c => c.ScheduledTime)
                                                     .FirstOrDefault();

            var stats = new
            {
                Total = allProspects.Count,
                Pending = allProspects.Count(p => p.Status == "Pending"),
                Sent = allProspects.Count(p => p.Status == "Sent"),
                Failed = allProspects.Count(p => p.Status == "Failed"),
                RecentSent = allProspects.Where(p => p.Status == "Sent").OrderByDescending(p => p.SentDate).Take(10),
                LastJobRun = lastJobRun == default ? (DateTime?)null : lastJobRun,
                NextJobScheduled = nextJobScheduled == default ? (DateTime?)null : nextJobScheduled
            };

            return Ok(stats);
        }
    }
}
