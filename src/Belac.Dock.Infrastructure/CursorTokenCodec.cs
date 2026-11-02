using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Belac.Dock.Infrastructure;

public static class CursorTokenCodec
{
    private const string Prefix = "cursor-safe-storage:v1:";
    public static string? Decode(string? stored, Func<byte[], byte[]>? unprotect = null)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (!stored.StartsWith("cursor-safe-storage:", StringComparison.Ordinal)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unsupported Cursor credential encoding.");
        if (stored.Length > 65536) throw new InvalidDataException("Cursor credential exceeds the size limit.");
        var encrypted = Convert.FromBase64String(stored[Prefix.Length..]);
        byte[] clear;
        if (unprotect is not null) clear = unprotect(encrypted);
        else
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (encrypted.AsSpan().StartsWith("v10"u8))
            {
                if (encrypted.Length < 31) throw new InvalidDataException("Invalid Cursor credential envelope.");
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "Local State");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Cursor state exceeds the size limit.");
                using var state = JsonDocument.Parse(stream);
                var wrappedKey = Convert.FromBase64String(state.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!);
                if (!wrappedKey.AsSpan().StartsWith("DPAPI"u8)) throw new InvalidDataException("Unsupported Cursor key encoding.");
                var key = ProtectedData.Unprotect(wrappedKey[5..], null, DataProtectionScope.CurrentUser);
                clear = new byte[encrypted.Length - 31];
                try
                {
                    using var aes = new AesGcm(key, 16);
                    aes.Decrypt(encrypted.AsSpan(3, 12), encrypted.AsSpan(15, clear.Length), encrypted.AsSpan(encrypted.Length - 16), clear);
                }
                catch { CryptographicOperations.ZeroMemory(clear); throw; }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            else clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        }
        try { return new UTF8Encoding(false, true).GetString(clear).TrimEnd('\0'); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}

