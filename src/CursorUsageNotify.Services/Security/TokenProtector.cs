using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CursorUsageNotify.Services.Security;

/// <summary>
/// 使用 Windows DPAPI 加密/解密 session token，避免明文落盘。
/// 仅 Windows 可用；加密后的字节写入 secrets.dat。
/// </summary>
public sealed class TokenProtector
{
    private readonly ILogger<TokenProtector> _logger;
    private readonly byte[] _additionalEntropy = Encoding.UTF8.GetBytes("CursorUsageNotify.v1");

    public TokenProtector(ILogger<TokenProtector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 加密明文 token。
    /// </summary>
    public byte[] Encrypt(string plainToken)
    {
        if (string.IsNullOrEmpty(plainToken))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainToken),
                _additionalEntropy,
                DataProtectionScope.CurrentUser);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "DPAPI 不可用，当前平台不支持 ProtectedData。token 将以明文存储。");
            return Encoding.UTF8.GetBytes(plainToken);
        }
    }

    /// <summary>
    /// 解密 token。
    /// </summary>
    public string Decrypt(byte[]? cipher)
    {
        if (cipher is null || cipher.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(cipher, _additionalEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "DPAPI 解密失败，可能是不同用户/机器。请重新输入 session token。");
            return string.Empty;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogWarning(ex, "DPAPI 不可用，按明文回退。");
            return Encoding.UTF8.GetString(cipher);
        }
    }
}
