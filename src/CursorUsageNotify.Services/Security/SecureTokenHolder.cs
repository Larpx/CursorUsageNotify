using System.Security.Cryptography;
using System.Text;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Security
{
    /// <summary>
    /// 安全持有 session token：内部以 UTF8 字节形式存储，使用后立即 <see cref="CryptographicOperations.ZeroMemory"/>。
    /// 避免明文 string 长期驻留内存；UI 层不应直接读取真实值，仅通过 <see cref="HasToken"/> 判断状态。
    /// </summary>
    public sealed class SecureTokenHolder : IDisposable
    {
        private byte[]? _bytes;
        private readonly object _lock = new();

        /// <summary>
        /// 是否已持有非空 token。
        /// </summary>
        public bool HasToken
        {
            get
            {
                lock (_lock)
                {
                    return _bytes is { Length: > 0 };
                }
            }
        }

        /// <summary>
        /// 从字符串设置 token：内部转 UTF8 字节后立即清除临时副本。
        /// 调用后输入字符串由 GC 回收（string 不可变，无法主动清除）。
        /// </summary>
        /// <param name="token">
        /// 明文 token 字符串；空值等同于 <see cref="Clear"/>。
        /// </param>
        public void Set(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                Clear();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(token);
            try
            {
                SetBytes(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// 从 UTF8 字节设置 token。
        /// 内部克隆一份以确保独占控制生命周期，调用方可立即清除自有副本。
        /// </summary>
        /// <param name="utf8Bytes">
        /// UTF8 编码的 token 字节。
        /// </param>
        public void SetBytes(byte[] utf8Bytes)
        {
            lock (_lock)
            {
                ClearCore();
                _bytes = utf8Bytes is { Length: > 0 } ? (byte[])utf8Bytes.Clone() : null;
            }
        }

        /// <summary>
        /// 借用明文 token 执行异步操作（通常是 API 调用）。
        /// 内部临时解码为 string 传给 <paramref name="action"/>；string 在作用域结束后由 GC 回收。
        /// </summary>
        /// <typeparam name="T">
        /// 操作返回类型。
        /// </typeparam>
        /// <param name="action">
        /// 接收明文 token 的异步操作。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        /// <returns>
        /// 操作返回值。
        /// </returns>
        public async Task<T> UseAsync<T>(Func<string, CancellationToken, Task<T>> action, CancellationToken ct)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            string temp;
            lock (_lock)
            {
                if (_bytes is null || _bytes.Length == 0)
                {
                    throw new InvalidOperationException("Token 未设置，无法借用。");
                }
                temp = Encoding.UTF8.GetString(_bytes);
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
        /// 借用明文 token 执行无返回值的异步操作（如加密落盘）。
        /// 委托到泛型 <see cref="UseAsync{T}"/> 复用锁与清理逻辑。
        /// </summary>
        /// <param name="action">
        /// 接收明文 token 的异步操作。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        public Task UseAsync(Func<string, CancellationToken, Task> action, CancellationToken ct)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            return UseAsync<bool>(async (token, t) =>
            {
                await action(token, t);
                return true;
            }, ct);
        }

        /// <summary>
        /// 清除内存中的 token 字节并置 null。
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                ClearCore();
            }
        }

        private void ClearCore()
        {
            if (_bytes is not null)
            {
                CryptographicOperations.ZeroMemory(_bytes);
                _bytes = null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Clear();
        }
    }
}
