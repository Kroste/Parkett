using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Parkett.Licensing;

public enum LicenseStatus
{
    Valid,
    Malformed,
    SignatureInvalid,
    Expired,
}

public sealed record LicenseCheckResult(LicenseStatus Status, LicenseKey? License)
{
    public bool IsValid => Status == LicenseStatus.Valid && License is not null;
}

/// <summary>
/// Prüft Lizenzschlüssel gegen einen fest eingebauten öffentlichen ECDSA-P-256-Schlüssel.
/// Der private Schlüssel bleibt beim Verkaufsprozess und gehört NIE ins Repository.
/// </summary>
public sealed class LicenseVerifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _publicKeyBase64;

    public LicenseVerifier(string publicKeyBase64)
    {
        _publicKeyBase64 = publicKeyBase64 ?? throw new ArgumentNullException(nameof(publicKeyBase64));
    }

    /// <summary>True, wenn überhaupt ein öffentlicher Schlüssel hinterlegt ist.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_publicKeyBase64);

    public LicenseCheckResult Check(string key, DateTimeOffset now)
    {
        if (!IsConfigured)
        {
            Log.Warn("Lizenzprüfung ohne hinterlegten öffentlichen Schlüssel aufgerufen.");
            return new LicenseCheckResult(LicenseStatus.Malformed, null);
        }

        if (!LicenseKey.TrySplit(key, out var payloadBytes, out var signatureBytes))
        {
            return new LicenseCheckResult(LicenseStatus.Malformed, null);
        }

        var payloadText = LicenseKey.DecodePayloadText(payloadBytes);

        if (!LicenseKey.TryParsePayload(payloadText, out var license))
        {
            return new LicenseCheckResult(LicenseStatus.Malformed, null);
        }

        if (!VerifySignature(payloadBytes, signatureBytes))
        {
            Log.Info("Lizenzschlüssel mit ungültiger Signatur abgelehnt.");
            return new LicenseCheckResult(LicenseStatus.SignatureInvalid, null);
        }

        if (license.IsExpired(now))
        {
            return new LicenseCheckResult(LicenseStatus.Expired, license);
        }

        return new LicenseCheckResult(LicenseStatus.Valid, license);
    }

    private bool VerifySignature(byte[] payload, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKeyBase64), out _);
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            Log.Warn(ex, "Signaturprüfung fehlgeschlagen.");
            return false;
        }
    }

    /// <summary>
    /// Signiert Nutzdaten. Nur für den Verkaufsprozess und die Tests — der Aufrufer hält den
    /// privaten Schlüssel. Steht hier, damit Prüf- und Erzeugungsformat nicht auseinanderlaufen.
    /// </summary>
    public static string Sign(LicenseKey license, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(privateKey);

        var payload = Encoding.UTF8.GetBytes(license.ToPayload());
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256);

        return LicenseKey.Encode(payload, signature);
    }
}
