using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Parkett.Persistence;

/// <summary>
/// Windows: DPAPI (CurrentUser). Linux/macOS: AES-256-GCM mit lokalem Master-Key
/// unter <c>~/.config/Parkett/protect.key</c> (Dateirechte 0600).
/// Chiffratformat: <c>nonce(12) | tag(16) | cipher</c>.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    public const string Prefix = "ENC1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _keyPath;

    public SecretProtector(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _keyPath = Path.Combine(dataDirectory, "protect.key");
    }

    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var cipher = OperatingSystem.IsWindows() ? ProtectWindows(bytes) : ProtectAes(bytes);

            return Prefix + Convert.ToBase64String(cipher);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Wert konnte nicht verschlüsselt werden — er wird NICHT im Klartext gespeichert.");
            return string.Empty;
        }
    }

    public string? Unprotect(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return null;
        }

        // Altbestand ohne Präfix: Klartext übernehmen und beim nächsten Speichern migrieren.
        if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return storedValue;
        }

        try
        {
            var cipher = Convert.FromBase64String(storedValue[Prefix.Length..]);
            var plain = OperatingSystem.IsWindows() ? UnprotectWindows(cipher) : UnprotectAes(cipher);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            // Nutzerwechsel oder verlorenes DPAPI-Profil: Eintrag verwerfen, nicht abstürzen.
            Log.Warn(ex, "Verschlüsselter Wert nicht lesbar — wird verworfen.");
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plain) =>
        ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);

    private byte[] ProtectAes(byte[] plain)
    {
        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var result = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        cipher.CopyTo(result, NonceSize + TagSize);

        return result;
    }

    private byte[] UnprotectAes(byte[] stored)
    {
        if (stored.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Chiffrat zu kurz.");
        }

        var key = LoadOrCreateKey();
        var nonce = stored.AsSpan(0, NonceSize);
        var tag = stored.AsSpan(NonceSize, TagSize);
        var cipher = stored.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return plain;
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
        {
            return File.ReadAllBytes(_keyPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(_keyPath, key);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Log.Info("Neuer lokaler Schutzschlüssel angelegt: {Path}", _keyPath);
        return key;
    }
}
