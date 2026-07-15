using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// 平台筛选下拉项。Display 为 UI 显示文本，Platform 为筛选值（null 表示全部平台）。
    /// </summary>
    public sealed class PlatformFilterOption
    {
        /// <summary>
        /// UI 显示文本（"全部" / "Cursor" / "DeepSeek"）。
        /// </summary>
        public string Display { get; init; } = string.Empty;

        /// <summary>
        /// 平台筛选值；null 表示不按平台筛选（查询所有平台）。
        /// </summary>
        public PlatformType? Platform { get; init; }

        /// <inheritdoc/>
        public override string ToString() => Display;

        /// <summary>
        /// 默认筛选选项列表："全部" + 各已支持平台。
        /// </summary>
        /// <returns>默认选项列表，第一项为"全部"。</returns>
        public static List<PlatformFilterOption> DefaultOptions() => new()
        {
            new PlatformFilterOption { Display = "全部", Platform = null },
            new PlatformFilterOption { Display = "Cursor", Platform = PlatformType.Cursor },
            new PlatformFilterOption { Display = "DeepSeek", Platform = PlatformType.DeepSeek }
        };
    }
}
