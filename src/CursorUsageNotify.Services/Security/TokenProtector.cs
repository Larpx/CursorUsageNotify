using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Security
{
    /// <summary>
    /// 使用 Windows DPAPI 加密/解密 session token，避免明文落盘。
    /// 仅 Windows 可用；加密后的字节写入 secrets.dat。
    /// DPAPI 不可用时拒绝明文降级，抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    public sealed class TokenProtector
    {
        private readonly ILogger<TokenProtector> _logger;
        private readonly byte[] _additionalEntropy = Encoding.UTF8.GetBytes("CursorUsageNotify.v1");

        /// <summary>
        /// 初始化 <see cref="TokenProtector"/> 实例。
        /// </summary>
        public TokenProtector(ILogger<TokenProtector> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 加密明文 token 字符串。
        /// 内部转换为 UTF8 字节后委托 <see cref="Encrypt(byte[])"/>。
        /// </summary>
        /// <param name="plainToken">
        /// 明文 token 字符串。
        /// </param>
        /// <returns>
        /// DPAPI 加密后的密文字节；输入为空时返回空数组。
        /// </returns>
        public byte[] Encrypt(string plainToken)
        {
            if (string.IsNullOrEmpty(plainToken))
            {
                return Array.Empty<byte>();
            }

            // 复制到独立 byte[]，避免上层字符串驻留；调用后由 GC 回收
            var plainBytes = Encoding.UTF8.GetBytes(plainToken);
            try
            {
                return Encrypt(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        /// <summary>
        /// 加密明文字节（UTF8 编码的 token）。
        /// 调用方负责在传入后立即 <see cref="CryptographicOperations.ZeroMemory"/> 自有副本。
        /// </summary>
        /// <param name="plainBytes">
        /// UTF8 编码的明文字节。
        /// </param>
        /// <returns>
        /// DPAPI 加密后的密文字节；输入为空时返回空数组。
        /// </returns>
        public byte[] Encrypt(byte[] plainBytes)
        {
            if (plainBytes is null || plainBytes.Length == 0)
            {
                return Array.Empty<byte>();
            }

            try
            {
                return ProtectedData.Protect(plainBytes, _additionalEntropy, DataProtectionScope.CurrentUser);
            }
            catch (PlatformNotSupportedException ex)
            {
                // DPAPI 不可用时拒绝明文存储，而非静默降级
                _logger.LogCritical(ex, "DPAPI 不可用，当前平台不支持 ProtectedData。拒绝明文存储 token。");
                throw new InvalidOperationException("当前平台不支持 DPAPI 加密，拒绝明文存储 token。", ex);
            }
        }

        /// <summary>
        /// 解密 token 为明文字节（UTF8）。
        /// 供 <see cref="SecureTokenHolder"/> 直接接管字节，避免经过 string。
        /// </summary>
        /// <param name="cipher">
        /// DPAPI 密文字节。
        /// </param>
        /// <returns>
        /// UTF8 编码的明文字节；输入为空或解密失败时返回空数组。
        /// </returns>
        public byte[] DecryptToBytes(byte[]? cipher)
        {
            if (cipher is null || cipher.Length == 0)
            {
                return Array.Empty<byte>();
            }

            try
            {
                return ProtectedData.Unprotect(cipher, _additionalEntropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "DPAPI 解密失败，可能是不同用户/机器。请重新输入 session token。");
                return Array.Empty<byte>();
            }
            catch (PlatformNotSupportedException ex)
            {
                // DPAPI 不可用时拒绝明文回退
                _logger.LogCritical(ex, "DPAPI 不可用，拒绝按明文回退。");
                throw new InvalidOperationException("当前平台不支持 DPAPI 加密，拒绝明文存储 token。", ex);
            }
        }

        /// <summary>
        /// 解密 token 为字符串。
        /// 保留供测试与兼容场景使用；运行时优先用 <see cref="DecryptToBytes"/> 避免字符串驻留。
        /// </summary>
        /// <param name="cipher">
        /// DPAPI 密文字节。
        /// </param>
        /// <returns>
        /// 明文 token 字符串；输入为空或解密失败时返回空字符串。
        /// </returns>
        public string Decrypt(byte[]? cipher)
        {
            var bytes = DecryptToBytes(cipher);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }
}
