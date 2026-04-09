using System.Text.Json.Serialization;

namespace EasyApplyAPI.Models
{
    public class Prospect
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("emailAddress")]
        public string EmailAddress { get; set; } = string.Empty;

        [JsonPropertyName("campaignId")]
        public string CampaignId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "Pending"; // Pending, Sent, Failed

        [JsonPropertyName("sentDate")]
        public DateTime? SentDate { get; set; }


        [JsonPropertyName("companyName")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("managerName")]
        public string? ManagerName { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "Prospect";
    }
}
