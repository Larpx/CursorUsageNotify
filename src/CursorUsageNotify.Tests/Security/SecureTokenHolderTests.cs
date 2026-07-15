using System;
using System.Threading;
using System.Threading.Tasks;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Xunit;


namespace Larpx.PersonalTools.CursorUsageNotify.Tests.Security
{
    /// <summary>
    /// SecureTokenHolder 单元测试：覆盖 Set/SetBytes/UseAsync/Clear/Dispose 与 byte[] 独立性。
    /// </summary>
    public class SecureTokenHolderTests
    {
        [Fact]
        public void Set_ValidToken_HasTokenTrue()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("abc123");
            Assert.True(holder.HasToken());
        }

        [Fact]
        public void Set_EmptyString_HasTokenFalse()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("abc123");
            holder.Set(string.Empty);
            Assert.False(holder.HasToken());
        }

        [Fact]
        public void Set_NullString_HasTokenFalse()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("abc123");
            holder.Set(null!);
            Assert.False(holder.HasToken());
        }

        [Fact]
        public void Set_OverwritesPreviousToken_HasTokenTrue()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("first");
            holder.Set("second");
            Assert.True(holder.HasToken());
        }

        [Fact]
        public void SetBytes_ValidBytes_HasTokenTrue()
        {
            using var holder = new SecureTokenHolder();
            holder.SetBytes(System.Text.Encoding.UTF8.GetBytes("token-bytes"));
            Assert.True(holder.HasToken());
        }

        [Fact]
        public void SetBytes_EmptyArray_HasTokenFalse()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("abc123");
            holder.SetBytes(Array.Empty<byte>());
            Assert.False(holder.HasToken());
        }

        [Fact]
        public void SetBytes_NullArray_HasTokenFalse()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("abc123");
            holder.SetBytes(null!);
            Assert.False(holder.HasToken());
        }

        [Fact]
        public async Task UseAsync_WithToken_ReturnsActionResult()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("my-secret-token");

            var result = await holder.UseAsync(
                (token, ct) => Task.FromResult(token.Length),
                CancellationToken.None);

            Assert.Equal("my-secret-token".Length, result);
        }

        [Fact]
        public async Task UseAsync_NoToken_ThrowsInvalidOperationException()
        {
            using var holder = new SecureTokenHolder();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                holder.UseAsync(
                    (token, ct) => Task.FromResult(0),
                    CancellationToken.None));
        }

        [Fact]
        public async Task UseAsync_NullAction_ThrowsArgumentNullException()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("token");
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                holder.UseAsync<int>(null!, CancellationToken.None));
        }

        [Fact]
        public void Clear_AfterSet_HasTokenFalse()
        {
            using var holder = new SecureTokenHolder();
            holder.Set("abc123");
            holder.Clear();
            Assert.False(holder.HasToken());
        }

        [Fact]
        public void Clear_WhenEmpty_NoOp()
        {
            using var holder = new SecureTokenHolder();
            holder.Clear();
            Assert.False(holder.HasToken());
        }

        [Fact]
        public void Dispose_AfterSet_HasTokenFalse()
        {
            var holder = new SecureTokenHolder();
            holder.Set("abc123");
            holder.Dispose();
            Assert.False(holder.HasToken());
        }

        [Fact]
        public async Task SetBytes_CallerClearsOwnArray_HolderStillValid()
        {
            using var holder = new SecureTokenHolder();
            var bytes = System.Text.Encoding.UTF8.GetBytes("owned-by-caller");
            holder.SetBytes(bytes);
            Array.Clear(bytes, 0, bytes.Length);

            Assert.True(holder.HasToken());
            var retrieved = await holder.UseAsync((t, _) => Task.FromResult(t), CancellationToken.None);
            Assert.Equal("owned-by-caller", retrieved);
        }
    }
}
