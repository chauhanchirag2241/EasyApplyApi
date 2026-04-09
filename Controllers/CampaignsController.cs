using EasyApplyAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Hangfire;
using EasyApplyAPI.Jobs;

namespace EasyApplyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CampaignsController : ControllerBase
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;

        public CampaignsController(CosmosClient cosmosClient, IConfiguration configuration)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
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
            
            if (campaign.ScheduleType == "Daily" && !string.IsNullOrEmpty(campaign.ScheduledTimeOfDay))
            {
                var timeParts = campaign.ScheduledTimeOfDay.Split(':');
                int hour = int.Parse(timeParts[0]);
                int minute = int.Parse(timeParts[1]);
                RecurringJob.AddOrUpdate<CampaignDailyProcessor>(campaign.Id, x => x.ProcessDaily(campaign.Id), Cron.Daily(hour, minute));
            }
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

                bool scheduleChanged = false;
                
                if (updateData.ScheduleType == "Daily")
                {
                    if (campaign.ScheduleType != "Daily" || campaign.ScheduledTimeOfDay != updateData.ScheduledTimeOfDay)
                    {
                        scheduleChanged = true;
                    }
                    campaign.ScheduleType = "Daily";
                    campaign.ScheduledTimeOfDay = updateData.ScheduledTimeOfDay;
                }
                else
                {
                    if (campaign.ScheduleType != "Once" || campaign.ScheduledTime != updateData.ScheduledTime)
                    {
                        scheduleChanged = true;
                    }
                    campaign.ScheduleType = "Once";
                    campaign.ScheduledTime = updateData.ScheduledTime;
                }
                
                await container.ReplaceItemAsync(campaign, id, new PartitionKey(id));

                if (scheduleChanged)
                {
                    if (campaign.ScheduleType == "Daily")
                    {
                         // Remove any potential old pending jobs we had if we switched to Daily from Once
                         var query = container.GetItemLinqQueryable<Prospect>(true)
                             .Where(p => p.Type == "Prospect" && p.CampaignId == id && p.Status == "Pending")
                             .ToFeedIterator();
                         while(query.HasMoreResults)
                         {
                             foreach(var p in await query.ReadNextAsync())
                             {
                                 if (p.HangfireJobId != null) 
                                 {
                                     BackgroundJob.Delete(p.HangfireJobId);
                                     p.HangfireJobId = null;
                                     await container.ReplaceItemAsync(p, p.Id, new PartitionKey(p.Id));
                                 }
                             }
                         }

                         var timeParts = campaign.ScheduledTimeOfDay!.Split(':');
                         int hour = int.Parse(timeParts[0]);
                         int minute = int.Parse(timeParts[1]);
                         RecurringJob.AddOrUpdate<CampaignDailyProcessor>(campaign.Id, x => x.ProcessDaily(campaign.Id), Cron.Daily(hour, minute));
                    }
                    else
                    {
                         RecurringJob.RemoveIfExists(campaign.Id);

                         // Find all pending prospects and reschedule them
                         var query = container.GetItemLinqQueryable<Prospect>(true)
                             .Where(p => p.Type == "Prospect" && p.CampaignId == id && p.Status == "Pending")
                             .ToFeedIterator();

                         var sendDelay = campaign.ScheduledTime - DateTime.UtcNow;

                         while (query.HasMoreResults)
                         {
                             foreach (var prospect in await query.ReadNextAsync())
                             {
                                 // Delete old job
                                 if (prospect.HangfireJobId != null)
                                 {
                                     BackgroundJob.Delete(prospect.HangfireJobId);
                                 }

                                 // Schedule new job
                                 if (sendDelay.TotalMinutes <= 0)
                                 {
                                     prospect.HangfireJobId = BackgroundJob.Enqueue<SendEmailJob>(x => x.ProcessCampaignEmail(campaign.Id, prospect.Id));
                                 }
                                 else
                                 {
                                     prospect.HangfireJobId = BackgroundJob.Schedule<SendEmailJob>(x => x.ProcessCampaignEmail(campaign.Id, prospect.Id), sendDelay);
                                 }
                                 
                                 // Save updated prospect
                                 await container.ReplaceItemAsync(prospect, prospect.Id, new PartitionKey(prospect.Id));
                             }
                         }
                    }
                }

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
                RecurringJob.RemoveIfExists(id);
                // Delete all prospects and cancel jobs
                var query = container.GetItemLinqQueryable<Prospect>(true)
                    .Where(p => p.Type == "Prospect" && p.CampaignId == id)
                    .ToFeedIterator();

                while (query.HasMoreResults)
                {
                    foreach (var prospect in await query.ReadNextAsync())
                    {
                        if (prospect.HangfireJobId != null)
                        {
                            BackgroundJob.Delete(prospect.HangfireJobId);
                        }
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
            if (file == null || file.Length == 0) return BadRequest("File is empty");

            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);

            var safeFileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsPath, Guid.NewGuid() + "_" + safeFileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { ResumeFilePath = filePath });
        }
    }
}
