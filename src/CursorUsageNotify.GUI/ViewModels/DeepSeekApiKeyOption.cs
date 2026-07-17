
namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// DeepSeek API Key 下拉选项（设置页单 Key 模式）。
    /// </summary>
    public sealed class DeepSeekApiKeyOption
    {
        /// <summary>
        /// 稳定追踪 ID（对应用量事件 Kind）。
        /// </summary>
        public string TrackingId { get; init; } = string.Empty;

        /// <summary>
        /// 显示名称。
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// ComboBox 显示文本。
        /// </summary>
        public string Display => string.IsNullOrEmpty(Name) || Name == TrackingId
            ? TrackingId
            : $"{Name}（{TrackingId}）";

        /// <inheritdoc/>
        public override string ToString() => Display;
    }
}
