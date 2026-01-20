using System.Security.Cryptography;
using M365MailMirror.Core.Authentication;

namespace M365MailMirror.Infrastructure.Authentication;

/// <summary>
/// File-based token cache storage with encryption using DPAPI on Windows
/// or file-system encryption on other platforms.
/// </summary>
public class FileTokenCacheStorage : ITokenCacheStorage
{
    private readonly string _cacheFilePath;
    private readonly object _fileLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTokenCacheStorage"/> class.
    /// </summary>
    /// <param name="cacheFilePath">Optional custom cache file path. Defaults to user's config directory.</param>
    public FileTokenCacheStorage(string? cacheFilePath = null)
    {
        _cacheFilePath = cacheFilePath ?? GetDefaultCachePath();
    }

    /// <inheritdoc />
    public string StorageDescription => OperatingSystem.IsWindows()
        ? $"Windows DPAPI encrypted file: {_cacheFilePath}"
        : $"Encrypted file: {_cacheFilePath}";

    /// <inheritdoc />
    public Task<byte[]?> ReadAsync()
    {
        lock (_fileLock)
        {
            if (!File.Exists(_cacheFilePath))
            {
                return Task.FromResult<byte[]?>(null);
            }

            try
            {
                var encryptedData = File.ReadAllBytes(_cacheFilePath);
                var data = Unprotect(encryptedData);
                return Task.FromResult<byte[]?>(data);
            }
            catch
            {
                // If decryption fails, return null (will require re-authentication)
                return Task.FromResult<byte[]?>(null);
            }
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(byte[] data)
    {
        lock (_fileLock)
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encryptedData = Protect(data);
            File.WriteAllBytes(_cacheFilePath, encryptedData);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        lock (_fileLock)
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync()
    {
        return Task.FromResult(File.Exists(_cacheFilePath));
    }

    private static string GetDefaultCachePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".config", "m365-mail-mirror", "token_cache.dat");
    }

    private static byte[] Protect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use DPAPI on Windows for user-level encryption
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }

        // On non-Windows platforms, use a simple local encryption
        // In production, this would ideally use platform-specific secret storage
        // (macOS Keychain, Linux Secret Service), but file encryption provides
        // a reasonable baseline for cross-platform support
        return EncryptWithMachineKey(data);
    }

    private static byte[] Unprotect(byte[] encryptedData)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
        }

        return DecryptWithMachineKey(encryptedData);
    }

    private static byte[] EncryptWithMachineKey(byte[] data)
    {
        // Generate a key from machine-specific data
        var key = DeriveKeyFromMachineInfo();
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length);

        using (var cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cryptoStream.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static byte[] DecryptWithMachineKey(byte[] encryptedData)
    {
        var key = DeriveKeyFromMachineInfo();
        var iv = new byte[16];
        Array.Copy(encryptedData, 0, iv, 0, 16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var ms = new MemoryStream();
        using (var cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
        {
            cryptoStream.Write(encryptedData, 16, encryptedData.Length - 16);
        }

        return ms.ToArray();
    }

    private static byte[] DeriveKeyFromMachineInfo()
    {
        // Derive a key from machine-specific information
        // This ensures the token cache is tied to this specific machine/user
        var machineId = Environment.MachineName + Environment.UserName;
        var salt = "m365-mail-mirror-token-cache"u8.ToArray();
        var password = System.Text.Encoding.UTF8.GetBytes(machineId);

        return Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA256, 32);
    }
}
