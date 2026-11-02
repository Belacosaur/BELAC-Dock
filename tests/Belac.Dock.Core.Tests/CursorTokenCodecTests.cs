using System.Security.Cryptography;
using System.Text;
using Belac.Dock.Infrastructure;

namespace Belac.Dock.Core.Tests;

public class CursorTokenCodecTests
{
    [Fact]
    public void PlaintextAndMissingCredentialsRemainCompatible()
    {
        Assert.Null(CursorTokenCodec.Decode(null));
        Assert.Equal("legacy-token", CursorTokenCodec.Decode("legacy-token"));
    }

    [Fact]
    public void EncryptedCredentialsAreDecodedAndTemporaryPlaintextIsCleared()
    {
        var clear = Encoding.UTF8.GetBytes("test-token");
        Assert.Equal("test-token", CursorTokenCodec.Decode("cursor-safe-storage:v1:AQID", bytes =>
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
            return clear;
        }));
        Assert.All(clear, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("cursor-safe-storage:v2:AQID")]
    [InlineData("cursor-safe-storage:v1:invalid!")]
    public void UnsupportedOrCorruptCredentialsAreNeverReturnedAsTokens(string value)
    {
        Assert.ThrowsAny<Exception>(() => CursorTokenCodec.Decode(value));
    }

    [Fact]
    public void WindowsProtectedCredentialsRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return;
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes("test-only-token"), null, DataProtectionScope.CurrentUser);
        Assert.Equal("test-only-token", CursorTokenCodec.Decode("cursor-safe-storage:v1:" + Convert.ToBase64String(encrypted)));
    }
}
