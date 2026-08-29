using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Parkett.Tests;

/// <summary>
/// Der öffentliche Schlüssel in <c>App.axaml.cs</c> ist ein Base64-Literal, das von
/// Hand aus dem Lizenzwerkzeug hineinkopiert wird. Ein abgeschnittenes Zeichen fällt
/// beim Bauen nicht auf: <see cref="Parkett.Licensing.LicenseVerifier"/> fängt die
/// <see cref="CryptographicException"/> ab und lehnt jeden Schlüssel als ungültig ab.
/// Der Fehler zeigt sich dann erst beim Kunden, dessen bezahlte Lizenz nicht greift.
///
/// Der Test liest die Quelldatei, weil das Feld privat ist — dieselbe Bauart wie
/// <see cref="XamlResourceTests"/>.
/// </summary>
public class LicensePublicKeyTests
{
    [Fact]
    public void Der_eingebaute_oeffentliche_Schluessel_ist_hinterlegt()
    {
        PublicKey().Should().NotBeNullOrWhiteSpace(
            "ohne ihn läuft jede ausgestellte Lizenz ins Leere und alles bleibt die kostenlose Fassung");
    }

    [Fact]
    public void Der_eingebaute_oeffentliche_Schluessel_ist_ein_gueltiger_P256_Schluessel()
    {
        var bytes = Convert.FromBase64String(PublicKey());

        using var ecdsa = ECDsa.Create();
        var import = () => ecdsa.ImportSubjectPublicKeyInfo(bytes, out _);

        import.Should().NotThrow("die App importiert ihn bei jeder Lizenzprüfung genau so");
        ecdsa.KeySize.Should().Be(256, "das Format ist auf ECDSA P-256 festgelegt");
    }

    private static string PublicKey()
    {
        var quelle = File.ReadAllText(Path.Combine(RepoRoot(), "Parkett", "App.axaml.cs"));

        var treffer = Regex.Match(
            quelle,
            @"LicensePublicKey\s*=\s*""([^""]*)""",
            RegexOptions.Singleline);

        treffer.Success.Should().BeTrue("sonst wurde das Feld umbenannt und dieser Test prüft nichts mehr");

        return treffer.Groups[1].Value;
    }

    /// <summary>Vom Testausgabeverzeichnis nach oben, erkennbar an der Solution-Datei.</summary>
    private static string RepoRoot()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null)
        {
            if (File.Exists(Path.Combine(verzeichnis.FullName, "Parkett.slnx")))
            {
                return verzeichnis.FullName;
            }

            verzeichnis = verzeichnis.Parent;
        }

        throw new DirectoryNotFoundException("Parkett.slnx nicht gefunden — Repo-Wurzel nicht auflösbar.");
    }
}
