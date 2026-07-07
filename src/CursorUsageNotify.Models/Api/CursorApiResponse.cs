using System.Text.Json.Serialization;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Api
{
    /// <summary>
    /// /api/dashboard/get-filtered-usage-events 响应包。
    /// </summary>
    public sealed class CursorUsageEventsResponse
    {
        /// <summary>
        /// 当前周期事件总数（用于判断是否需要继续分页）。
        /// </summary>
        [JsonPropertyName("totalUsageEventsCount")]
        public int TotalUsageEventsCount { get; set; }

        /// <summary>
        /// 事件列表。
        /// </summary>
        [JsonPropertyName("usageEventsDisplay")]
        public List<CursorUsageEventDto> UsageEventsDisplay { get; set; } = new();

        /// <summary>
        /// 未识别字段。
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? ExtraFields { get; set; }
    }
}
