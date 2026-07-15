using System.Security.Cryptography;
using System.Text;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Security
{
    /// <summary>
    /// 安全持有多平台 session token：内部以 UTF8 字节形式存储，使用后立即 <see cref="CryptographicOperations.ZeroMemory"/>。
    /// 避免明文 string 长期驻留内存；UI 层不应直接读取真实值，仅通过 <see cref="HasToken(PlatformType)"/> 判断状态。
    /// </summary>
    /// <remarks>
    /// 向后兼容：无参重载默认操作 <see cref="PlatformType.Cursor"/>，供现有代码渐进迁移。
    /// </remarks>
    public sealed class SecureTokenHolder : IDisposable
    {
        private readonly Dictionary<PlatformType, byte[]> _tokens = new();
        private readonly object _lock = new();

        // ──────────────────────────────────────────────────────────────
        // 向后兼容 API（默认操作 Cursor 平台，供现有代码渐进迁移）
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 从字符串设置 Cursor 平台的 token（向后兼容）。
        /// </summary>
        /// <param name="token">明文 token 字符串；空值等同于 <see cref="Clear()"/>。</param>
        public void Set(string token) => Set(PlatformType.Cursor, token);

        /// <summary>
        /// 从 UTF8 字节设置 Cursor 平台的 token（向后兼容）。
        /// </summary>
        /// <param name="utf8Bytes">UTF8 编码的 token 字节。</param>
        public void SetBytes(byte[] utf8Bytes) => SetBytes(PlatformType.Cursor, utf8Bytes);

        /// <summary>
        /// 借用 Cursor 平台的明文 token 执行异步操作（向后兼容）。
        /// </summary>
        public Task<T> UseAsync<T>(Func<string, CancellationToken, Task<T>> action, CancellationToken ct)
            => UseAsync(PlatformType.Cursor, action, ct);

        /// <summary>
        /// 借用 Cursor 平台的明文 token 执行无返回值异步操作（向后兼容）。
        /// </summary>
        public Task UseAsync(Func<string, CancellationToken, Task> action, CancellationToken ct)
            => UseAsync(PlatformType.Cursor, action, ct);

        /// <summary>
        /// 清除 Cursor 平台的 token（向后兼容）。
        /// </summary>
        public void Clear() => Clear(PlatformType.Cursor);

        // ──────────────────────────────────────────────────────────────
        // 多平台 API
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 是否有任意平台已持有非空 token。
        /// </summary>
        public bool HasAnyToken
        {
            get
            {
                lock (_lock)
                {
                    return _tokens.Values.Any(b => b is { Length: > 0 });
                }
            }
        }

        /// <summary>
        /// 指定平台是否已持有非空 token。
        /// 默认参数为 <see cref="PlatformType.Cursor"/>，向后兼容无参调用。
        /// </summary>
        /// <param name="platform">平台类型，默认 Cursor。</param>
        /// <returns>已持有非空 token 时返回 true。</returns>
        public bool HasToken(PlatformType platform = PlatformType.Cursor)
        {
            lock (_lock)
            {
                return _tokens.TryGetValue(platform, out var bytes) && bytes is { Length: > 0 };
            }
        }

        /// <summary>
        /// 从字符串设置指定平台的 token：内部转 UTF8 字节后立即清除临时副本。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        /// <param name="token">明文 token 字符串；空值等同于 <see cref="Clear(PlatformType)"/>。</param>
        public void Set(PlatformType platform, string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                Clear(platform);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(token);
            try
            {
                SetBytes(platform, bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// 从 UTF8 字节设置指定平台的 token。
        /// 内部克隆一份以确保独占控制生命周期，调用方可立即清除自有副本。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        /// <param name="utf8Bytes">UTF8 编码的 token 字节。</param>
        public void SetBytes(PlatformType platform, byte[] utf8Bytes)
        {
            lock (_lock)
            {
                ClearCore(platform);
                _tokens[platform] = utf8Bytes is { Length: > 0 } ? (byte[])utf8Bytes.Clone() : Array.Empty<byte>();
            }
        }

        /// <summary>
        /// 借用指定平台的明文 token 执行异步操作（通常是 API 调用）。
        /// 内部临时解码为 string 传给 <paramref name="action"/>；string 在作用域结束后由 GC 回收。
        /// </summary>
        /// <typeparam name="T">操作返回类型。</typeparam>
        /// <param name="platform">平台类型。</param>
        /// <param name="action">接收明文 token 的异步操作。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>操作返回值。</returns>
        public async Task<T> UseAsync<T>(
            PlatformType platform,
            Func<string, CancellationToken, Task<T>> action,
            CancellationToken ct)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            string temp;
            lock (_lock)
            {
                if (!_tokens.TryGetValue(platform, out var bytes) || bytes is null || bytes.Length == 0)
                {
                    throw new InvalidOperationException($"平台 {platform} 的 token 未设置，无法借用。");
                }
                temp = Encoding.UTF8.GetString(bytes);
            }

            try
            {
                return await action(temp, ct);
            }
            finally
            {
                // string 不可变，无法主动 Clear；置 null 加速脱离引用
                temp = null!;
            }
        }

        /// <summary>
        /// 借用指定平台的明文 token 执行无返回值的异步操作（如加密落盘）。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        /// <param name="action">接收明文 token 的异步操作。</param>
        /// <param name="ct">取消令牌。</param>
        public Task UseAsync(
            PlatformType platform,
            Func<string, CancellationToken, Task> action,
            CancellationToken ct)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            return UseAsync<bool>(platform, async (token, t) =>
            {
                await action(token, t);
                return true;
            }, ct);
        }

        /// <summary>
        /// 清除指定平台的 token 字节。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        public void Clear(PlatformType platform)
        {
            lock (_lock)
            {
                ClearCore(platform);
            }
        }

        /// <summary>
        /// 清除所有平台的 token 字节。
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                foreach (var platform in _tokens.Keys.ToList())
                {
                    ClearCore(platform);
                }
            }
        }

        private void ClearCore(PlatformType platform)
        {
            if (_tokens.TryGetValue(platform, out var bytes) && bytes is not null)
            {
                if (bytes.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
                _tokens[platform] = Array.Empty<byte>();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ClearAll();
        }
    }
}
