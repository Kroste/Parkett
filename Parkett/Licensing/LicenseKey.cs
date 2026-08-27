using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Parkett.Licensing;

/// <summary>
/// Ein Lizenzschlüssel für den Direktverkauf: signierte Nutzdaten, offline prüfbar.
/// Format: <c>&lt;base64url(payload)&gt;.&lt;base64url(signatur)&gt;</c>.
/// Kein Aktivierungsserver — der wäre eine zusätzliche Fehlerquelle und ein Datenschutzthema,
/// ohne einen entschlossenen Cracker aufzuhalten.
/// </summary>
public sealed record LicenseKey(
    Edition Edition,
    string LicensedTo,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt)
{
    private const string PayloadVersion = "v1";

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && now > expiry;

    /// <summary>Erzeugt die zu signierenden Nutzdaten. Reihenfolge ist Teil des Formats.</summary>
    public string ToPayload()
    {
        var expiry = ExpiresAt?.ToUnixTimeSeconds() ?? 0L;

        return string.Join('|',
            PayloadVersion,
            Edition.ToString(),
            LicensedTo.Replace('|', '/'),
            IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            expiry.ToString(CultureInfo.InvariantCulture));
    }

    public static bool TryParsePayload(string payload, out LicenseKey license)
    {
        license = null!;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|');

        if (parts.Length != 5 || parts[0] != PayloadVersion)
        {
            return false;
        }

        if (!Enum.TryParse<Edition>(parts[1], ignoreCase: false, out var edition))
        {
            return false;
        }

        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var issued) ||
            !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiry))
        {
            return false;
        }

        license = new LicenseKey(
            edition,
            parts[2],
            DateTimeOffset.FromUnixTimeSeconds(issued),
            expiry == 0L ? null : DateTimeOffset.FromUnixTimeSeconds(expiry));

        return true;
    }

    /// <summary>Zerlegt "payload.signatur" in seine beiden Base64Url-Teile.</summary>
    public static bool TrySplit(string key, out byte[] payloadBytes, out byte[] signatureBytes)
    {
        payloadBytes = [];
        signatureBytes = [];

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        // Zeilenumbrüche und Leerzeichen tolerieren — Schlüssel werden aus E-Mails kopiert.
        var cleaned = new string(key.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var separator = cleaned.IndexOf('.');

        if (separator <= 0 || separator == cleaned.Length - 1)
        {
            return false;
        }

        return TryDecode(cleaned[..separator], out payloadBytes)
               && TryDecode(cleaned[(separator + 1)..], out signatureBytes);
    }

    public static string Encode(byte[] payload, byte[] signature) =>
        $"{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(signature)}";

    public static string DecodePayloadText(byte[] payload) => Encoding.UTF8.GetString(payload);

    private static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];

        try
        {
            bytes = Base64Url.DecodeFromChars(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
