using System;
using System.Text;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;


namespace Larpx.PersonalTools.CursorUsageNotify.Tests.Security
{
    /// <summary>
    /// TokenProtector 单元测试：覆盖 byte[]/string 往返、空输入、非法密文容错。
    /// DPAPI 在 Windows 测试机器上必可用，PlatformNotSupportedException 分支不测（平台限制）。
    /// </summary>
    public class TokenProtectorTests
    {
        private static TokenProtector CreateProtector() =>
            new(NullLogger<TokenProtector>.Instance);

        [Fact]
        public void Encrypt_DecryptToBytes_RoundTrip_ReturnsOriginalBytes()
        {
            var protector = CreateProtector();
            var plain = Encoding.UTF8.GetBytes("round-trip-secret");

            var cipher = protector.Encrypt(plain);
            var decrypted = protector.DecryptToBytes(cipher);

            Assert.Equal(plain, decrypted);
        }

        [Fact]
        public void EncryptString_Decrypt_RoundTrip_ReturnsOriginalString()
        {
            var protector = CreateProtector();
            const string plain = "my-session-token-value";

            var cipher = protector.Encrypt(plain);
            var decrypted = protector.Decrypt(cipher);

            Assert.Equal(plain, decrypted);
        }

        [Fact]
        public void Encrypt_DecryptString_RoundTrip_ReturnsOriginalString()
        {
            var protector = CreateProtector();
            const string plain = "interop-string-to-bytes";

            var cipher = protector.Encrypt(plain);
            var decryptedBytes = protector.DecryptToBytes(cipher);
            var decrypted = Encoding.UTF8.GetString(decryptedBytes);

            Assert.Equal(plain, decrypted);
        }

        [Fact]
        public void Encrypt_EmptyBytes_ReturnsEmptyArray()
        {
            var protector = CreateProtector();
            var cipher = protector.Encrypt(Array.Empty<byte>());
            Assert.Empty(cipher);
        }

        [Fact]
        public void Encrypt_NullBytes_ReturnsEmptyArray()
        {
            var protector = CreateProtector();
            var cipher = protector.Encrypt((byte[]?)null!);
            Assert.Empty(cipher);
        }

        [Fact]
        public void Encrypt_EmptyString_ReturnsEmptyArray()
        {
            var protector = CreateProtector();
            var cipher = protector.Encrypt(string.Empty);
            Assert.Empty(cipher);
        }

        [Fact]
        public void DecryptToBytes_NullCipher_ReturnsEmptyArray()
        {
            var protector = CreateProtector();
            var plain = protector.DecryptToBytes(null);
            Assert.Empty(plain);
        }

        [Fact]
        public void DecryptToBytes_EmptyCipher_ReturnsEmptyArray()
        {
            var protector = CreateProtector();
            var plain = protector.DecryptToBytes(Array.Empty<byte>());
            Assert.Empty(plain);
        }

        [Fact]
        public void DecryptToBytes_InvalidCipher_ReturnsEmptyArray()
        {
            var protector = CreateProtector();
            var invalidCipher = Encoding.UTF8.GetBytes("not-a-valid-dpapi-blob");

            var plain = protector.DecryptToBytes(invalidCipher);

            Assert.Empty(plain);
        }

        [Fact]
        public void Decrypt_InvalidCipher_ReturnsEmptyString()
        {
            var protector = CreateProtector();
            var invalidCipher = Encoding.UTF8.GetBytes("not-a-valid-dpapi-blob");

            var plain = protector.Decrypt(invalidCipher);

            Assert.Equal(string.Empty, plain);
        }

        [Fact]
        public void Encrypt_ProducesDifferentCipherThanPlaintext()
        {
            var protector = CreateProtector();
            var plain = Encoding.UTF8.GetBytes("secret-data");

            var cipher = protector.Encrypt(plain);

            Assert.NotEqual(plain, cipher);
            Assert.NotEmpty(cipher);
        }

        [Fact]
        public void Encrypt_TwiceProducesDifferentCiphers()
        {
            var protector = CreateProtector();
            var plain = Encoding.UTF8.GetBytes("same-input");

            var cipher1 = protector.Encrypt(plain);
            var cipher2 = protector.Encrypt(plain);

            Assert.NotEqual(cipher1, cipher2);
        }
    }
}
