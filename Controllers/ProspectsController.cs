using EasyApplyAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace EasyApplyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProspectsController : ControllerBase
    {
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;

        public ProspectsController(CosmosClient cosmosClient, IConfiguration configuration)
        {
            _cosmosClient = cosmosClient;
            _configuration = configuration;
        }

        private Container GetContainer() => _cosmosClient.GetContainer(
            _configuration["CosmosDb:DatabaseName"],
            _configuration["CosmosDb:ContainerName"]);

        public class ProspectInput 
        {
            public string Email { get; set; } = string.Empty;
            public string? Company { get; set; }
            public string? Manager { get; set; }
        }

        public class CreateProspectsRequest
        {
            public string CampaignId { get; set; } = string.Empty;
            public List<ProspectInput> Contacts { get; set; } = new();
        }

        [HttpPost]
        public async Task<IActionResult> AddProspects([FromBody] CreateProspectsRequest request)
        {
            var container = GetContainer();

            try
            {
                var uniqueContacts = request.Contacts
                    .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                    .GroupBy(c => c.Email.Trim().ToLower())
                    .Select(g => g.First())
                    .ToList();

                var existingQuery = container.GetItemLinqQueryable<Prospect>(true)
                    .Where(p => p.Type == "Prospect" && p.CampaignId == request.CampaignId)
                    .Select(p => p.EmailAddress)
                    .ToFeedIterator();

                var existingEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (existingQuery.HasMoreResults)
                {
                    foreach (var e in await existingQuery.ReadNextAsync())
                    {
                        existingEmails.Add(e);
                    }
                }

                int count = 0;
                foreach (var contactInput in uniqueContacts)
                {
                    if (existingEmails.Contains(contactInput.Email)) continue;

                    var prospect = new Prospect
                    {
                        CampaignId = request.CampaignId,
                        EmailAddress = contactInput.Email.Trim(),
                        CompanyName = contactInput.Company?.Trim(),
                        ManagerName = contactInput.Manager?.Trim(),
                        Status = "Pending"
                    };

                    await container.CreateItemAsync(prospect, new PartitionKey(prospect.Id));
                    count++;
                }
                return Ok(new { Message = $"Added {count} prospects." });
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return NotFound("Campaign not found");
            }
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetProspectsByCampaign(string campaignId)
        {
            var container = GetContainer();
            var query = container.GetItemLinqQueryable<Prospect>(true)
                .Where(p => p.Type == "Prospect" && p.CampaignId == campaignId)
                .ToFeedIterator();

            var prospects = new List<Prospect>();
            while (query.HasMoreResults)
            {
                foreach (var item in await query.ReadNextAsync()) prospects.Add(item);
            }

            return Ok(prospects);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProspect(string id, [FromBody] Prospect updateData)
        {
            var container = GetContainer();
            try
            {
                var existing = await container.ReadItemAsync<Prospect>(id, new PartitionKey(id));
                var prospect = existing.Resource;
                prospect.EmailAddress = updateData.EmailAddress;

                await container.ReplaceItemAsync(prospect, id, new PartitionKey(id));
                return Ok(prospect);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return NotFound();
            }
        }

        [HttpDelete("{id}/{campaignId}")]
        public async Task<IActionResult> DeleteProspect(string id, string campaignId)
        {
            var container = GetContainer();
            try
            {
                await container.DeleteItemAsync<Prospect>(id, new PartitionKey(id));
                return Ok();
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return NotFound();
            }
        }

        [HttpPost("{id}/{campaignId}/send")]
        public async Task<IActionResult> SendNow(string id, string campaignId)
        {
            var container = GetContainer();
            try
            {
                var existing = await container.ReadItemAsync<Prospect>(id, new PartitionKey(id));
                var prospect = existing.Resource;

                if (prospect.CampaignId != campaignId) return BadRequest("Campaign mismatch");

                prospect.Status = "Pending";

                await container.ReplaceItemAsync(prospect, id, new PartitionKey(id));

                return Ok(prospect);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return NotFound();
            }
        }
    }
}
