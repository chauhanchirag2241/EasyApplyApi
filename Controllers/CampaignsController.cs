using EasyApplyAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace EasyApplyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CampaignsController : ControllerBase
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly EasyApplyAPI.Services.IBlobStorageService _blobService;

        public CampaignsController(CosmosClient cosmosClient, IConfiguration configuration, EasyApplyAPI.Services.IBlobStorageService blobService)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
            _blobService = blobService;
        }

        private Container GetContainer() => _cosmosClient.GetContainer(
            _configuration["CosmosDb:DatabaseName"],
            _configuration["CosmosDb:ContainerName"]);

        [HttpPost]
        public async Task<IActionResult> CreateCampaign([FromBody] Campaign campaign)
        {
            campaign.Id = Guid.NewGuid().ToString();
            campaign.CreatedAt = DateTime.UtcNow;
            
            await GetContainer().CreateItemAsync(campaign, new PartitionKey(campaign.Id));
            
            return Ok(campaign);
        }

        [HttpGet]
        public async Task<IActionResult> GetCampaigns()
        {
            var container = GetContainer();
            var campaignsQuery = container.GetItemLinqQueryable<Campaign>(true)
                .Where(c => c.Type == "Campaign")
                .ToFeedIterator();
                
            var campaigns = new List<Campaign>();
            while (campaignsQuery.HasMoreResults)
            {
                foreach (var item in await campaignsQuery.ReadNextAsync())
                    campaigns.Add(item);
            }

            var result = new List<object>();
            foreach (var c in campaigns)
            {
                int prospectCount = await container.GetItemLinqQueryable<Prospect>(true)
                    .Where(p => p.Type == "Prospect" && p.CampaignId == c.Id)
                    .CountAsync();
                    
                result.Add(new {
                    Id = c.Id,
                    Subject = c.Subject,
                    Body = c.Body,
                    ScheduleType = c.ScheduleType,
                    ScheduledTime = c.ScheduledTime,
                    ScheduledTimeOfDay = c.ScheduledTimeOfDay,
                    ProspectCount = prospectCount
                });
            }
            
            return Ok(result.OrderByDescending(c => ((dynamic)c).ScheduledTime));
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCampaign(string id, [FromBody] Campaign updateData)
        {
            var container = GetContainer();
            try {
                var campaignResp = await container.ReadItemAsync<Campaign>(id, new PartitionKey(id));
                var campaign = campaignResp.Resource;
                
                campaign.Subject = updateData.Subject;
                campaign.Body = updateData.Body;

                if (updateData.ScheduleType == "Daily")
                {
                    campaign.ScheduleType = "Daily";
                    campaign.ScheduledTimeOfDay = updateData.ScheduledTimeOfDay;
                }
                else
                {
                    campaign.ScheduleType = "Once";
                    campaign.ScheduledTime = updateData.ScheduledTime;
                }
                
                await container.ReplaceItemAsync(campaign, id, new PartitionKey(id));

                return Ok(campaign);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) 
            {
                return NotFound();
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCampaign(string id)
        {
            var container = GetContainer();
            try {
                // Delete all prospects
                var query = container.GetItemLinqQueryable<Prospect>(true)
                    .Where(p => p.Type == "Prospect" && p.CampaignId == id)
                    .ToFeedIterator();

                while (query.HasMoreResults)
                {
                    foreach (var prospect in await query.ReadNextAsync())
                    {
                        await container.DeleteItemAsync<Prospect>(prospect.Id, new PartitionKey(prospect.Id));
                    }
                }

                // Delete the campaign
                await container.DeleteItemAsync<Campaign>(id, new PartitionKey(id));
                return Ok();
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) 
            {
                return NotFound();
            }
        }

        [HttpPost("resume")]
        public async Task<IActionResult> UploadResume(IFormFile file)
        {
            try
            {
                var fileUrl = await _blobService.UploadFileAsync(file);
                return Ok(new { ResumeFilePath = fileUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
