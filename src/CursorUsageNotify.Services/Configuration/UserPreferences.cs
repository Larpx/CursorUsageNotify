using System.Text.Json;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Configuration
{
    /// <summary>
    /// 用户偏好配置，持久化到独立 JSON 文件跨重启保留。
    /// </summary>
    public sealed class UserPreferences
    {
        /// <summary>
        /// 偏好文件的磁盘路径（Save 时写入此路径）。
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Token 全局显示格式。默认显示全部数字。
        /// </summary>
        public TokenDisplayMode TokenDisplayMode { get; set; } = TokenDisplayMode.FullNumber;

        /// <summary>
        /// 创建默认偏好实例。
        /// </summary>
        public UserPreferences()
        {
            FilePath = string.Empty;
        }

        /// <summary>
        /// 创建指定文件路径的偏好实例。
        /// </summary>
        /// <param name="filePath">偏好文件绝对路径。</param>
        public UserPreferences(string filePath)
        {
            FilePath = filePath;
        }

        /// <summary>
        /// 从文件加载用户偏好。文件不存在时返回默认值。
        /// </summary>
        /// <param name="filePath">偏好文件路径。</param>
        /// <returns>用户偏好实例。</returns>
        public static UserPreferences Load(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var prefs = JsonSerializer.Deserialize<UserPreferences>(json);
                    if (prefs is not null)
                    {
                        // 始终更新 FilePath 为当前配置中的路径
                        return new UserPreferences(filePath) { TokenDisplayMode = prefs.TokenDisplayMode };
                    }
                }
            }
            catch
            {
                // 文件损坏或格式错误时静默降级，返回默认值
            }

            return new UserPreferences(filePath);
        }

        /// <summary>
        /// 保存用户偏好到文件（覆盖写入）。
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}
