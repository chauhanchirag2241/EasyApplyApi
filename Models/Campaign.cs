using System.Text.Json.Serialization;

namespace EasyApplyAPI.Models
{
    public class Campaign
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("resumeFilePath")]
        public string? ResumeFilePath { get; set; }

        [JsonPropertyName("scheduledTime")]
        public DateTime ScheduledTime { get; set; }

        [JsonPropertyName("scheduleType")]
        public string ScheduleType { get; set; } = "Once"; // "Once" or "Daily"

        [JsonPropertyName("scheduledTimeOfDay")]
        public string? ScheduledTimeOfDay { get; set; } // e.g. "14:30"

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "Campaign";
    }
}
