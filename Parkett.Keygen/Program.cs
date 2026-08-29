using System.Globalization;
using System.Security.Cryptography;
using Parkett.Licensing;

namespace Parkett.Keygen;

/// <summary>
/// Werkzeug des Verkaufsprozesses: erzeugt das Signaturschlüsselpaar und stellt
/// Lizenzschlüssel aus. Läuft auf dem Rechner des Verkäufers, nie beim Kunden.
///
/// <b>Warum das ein eigenes Programm ist und nicht in der App steckt:</b> die App
/// darf den privaten Schlüssel nicht kennen. Sie prüft Signaturen mit dem
/// öffentlichen Schlüssel — wer den privaten mitliefert, liefert die Lizenzfabrik
/// gleich mit.
///
/// Signiert wird über <see cref="LicenseVerifier.Sign"/> aus der App selbst, damit
/// Erzeugung und Prüfung nicht auseinanderlaufen können.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Parkett-Lizenzwerkzeug

          schluesselpaar --out <datei.pem>
              Erzeugt ein neues ECDSA-P-256-Paar. Der private Schlüssel landet in
              <datei.pem>, der öffentliche wird ausgegeben — der gehört als
              App.LicensePublicKey in die App.

              ACHTUNG: Ein neues Paar macht ALLE bereits ausgestellten Schlüssel
              ungültig. Das hier läuft genau einmal.

          lizenz --key <datei.pem> --an "<Name>" [--stufe Full|Pro] [--laeuft-ab JJJJ-MM-TT]
              Stellt einen Lizenzschlüssel aus. Ohne --laeuft-ab gilt er unbefristet,
              ohne --stufe gilt Pro.

          pruefen --oeffentlich <base64> <schluessel>
              Prüft einen Schlüssel so, wie die App es täte. Für die Gegenprobe nach
              dem Ausstellen.
        """;

    private static int Main(string[] args)
    {
        try
        {
            return (args.FirstOrDefault() ?? "hilfe") switch
            {
                "schluesselpaar" => CreateKeyPair(args),
                "lizenz" => IssueLicense(args),
                "pruefen" => Verify(args),
                _ => Help(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine(Usage);
        return 0;
    }

    private static int CreateKeyPair(string[] args)
    {
        var target = Required(args, "--out");
        var full = Path.GetFullPath(target);

        if (File.Exists(full))
        {
            // Überschreiben würde jede bereits ausgestellte Lizenz entwerten, und zwar
            // unwiederbringlich — der alte private Schlüssel wäre weg.
            Console.Error.WriteLine(
                $"Fehler: {full} existiert bereits. Ein zweites Paar würde alle bisher " +
                "ausgestellten Lizenzschlüssel entwerten. Wenn das gewollt ist, die Datei " +
                "vorher von Hand wegräumen.");
            return 1;
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, ecdsa.ExportPkcs8PrivateKeyPem());
        RestrictToOwner(full);

        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

        Console.WriteLine($"Privater Schlüssel: {full}");
        Console.WriteLine("  — gehört NICHT ins Repository und in ein Backup, das du nicht verlierst.");
        Console.WriteLine();
        Console.WriteLine("Öffentlicher Schlüssel für App.LicensePublicKey:");
        Console.WriteLine();
        Console.WriteLine(publicKey);

        return 0;
    }

    private static int IssueLicense(string[] args)
    {
        var keyFile = Required(args, "--key");
        var licensedTo = Required(args, "--an");
        var editionText = Optional(args, "--stufe") ?? nameof(Edition.Pro);
        var expiryText = Optional(args, "--laeuft-ab");

        if (!Enum.TryParse<Edition>(editionText, ignoreCase: true, out var edition))
        {
            throw new ArgumentException(
                $"Unbekannte Stufe '{editionText}'. Erlaubt: {string.Join(", ", Enum.GetNames<Edition>())}.");
        }

        if (edition == Edition.Free)
        {
            throw new ArgumentException(
                "Für die kostenlose Fassung braucht es keinen Schlüssel — sie ist der Zustand ohne.");
        }

        DateTimeOffset? expiry = null;

        if (expiryText is not null)
        {
            if (!DateOnly.TryParseExact(expiryText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                throw new ArgumentException($"--laeuft-ab erwartet JJJJ-MM-TT, nicht '{expiryText}'.");
            }

            // Ende des genannten Tages: ein Ablauf um 00:00 nimmt dem Kunden den letzten Tag.
            expiry = new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(Path.GetFullPath(keyFile)));

        var license = new LicenseKey(edition, licensedTo, DateTimeOffset.UtcNow, expiry);
        var key = LicenseVerifier.Sign(license, ecdsa);

        Console.WriteLine($"Stufe:       {license.Edition}");
        Console.WriteLine($"Lizenziert:  {license.LicensedTo}");
        Console.WriteLine($"Ausgestellt: {license.IssuedAt:yyyy-MM-dd}");
        Console.WriteLine($"Läuft ab:    {(expiry is null ? "unbefristet" : expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        Console.WriteLine();
        Console.WriteLine(key);

        return 0;
    }

    private static int Verify(string[] args)
    {
        var publicKey = Required(args, "--oeffentlich");
        var key = args.LastOrDefault()
                  ?? throw new ArgumentException("Der zu prüfende Schlüssel fehlt.");

        var result = new LicenseVerifier(publicKey).Check(key, DateTimeOffset.UtcNow);

        Console.WriteLine($"Ergebnis: {result.Status}");

        if (result.License is { } license)
        {
            Console.WriteLine($"  Stufe:      {license.Edition}");
            Console.WriteLine($"  Lizenziert: {license.LicensedTo}");
            Console.WriteLine($"  Läuft ab:   {(license.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unbefristet")}");
        }

        return result.IsValid ? 0 : 1;
    }

    /// <summary>
    /// Unter Linux/macOS die Rechte auf den Eigentümer eindampfen. Ein privater
    /// Schlüssel mit 644 im Home-Verzeichnis ist auf einem Mehrbenutzersystem
    /// dasselbe wie kein Schlüssel.
    /// </summary>
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows erbt die ACL des Benutzerprofils; ein Chmod-Äquivalent gibt es nicht.
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string Required(string[] args, string name) =>
        Optional(args, name) ?? throw new ArgumentException($"{name} fehlt.");

    private static string? Optional(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.Ordinal));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
