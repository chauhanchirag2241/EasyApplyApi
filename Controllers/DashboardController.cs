using EasyApplyAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using EasyApplyAPI.Services;

namespace EasyApplyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly IEmailProcessorService _emailProcessorService;

        public DashboardController(CosmosClient cosmosClient, IConfiguration configuration, IEmailProcessorService emailProcessorService)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
            _emailProcessorService = emailProcessorService;
        }

        private Container GetContainer() => _cosmosClient.GetContainer(
            _configuration["CosmosDb:DatabaseName"],
            _configuration["CosmosDb:ContainerName"]);

        [HttpPost("execute-all")]
        public IActionResult ExecuteAll()
        {
            _ = Task.Run(() => _emailProcessorService.ProcessAllPendingEmailsAsync());
            return Accepted(new { Message = "Email processing started in the background." });
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var container = GetContainer();
            // ... (rest of the existing stats code)
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

            var stats = new
            {
                Total = allProspects.Count,
                Pending = allProspects.Count(p => p.Status == "Pending"),
                Sent = allProspects.Count(p => p.Status == "Sent"),
                Failed = allProspects.Count(p => p.Status == "Failed"),
                RecentSent = allProspects.Where(p => p.Status == "Sent").OrderByDescending(p => p.SentDate).Take(10),
                LastJobRun = lastJobRun == default ? (DateTime?)null : lastJobRun
            };

            return Ok(stats);
        }
    }
}
