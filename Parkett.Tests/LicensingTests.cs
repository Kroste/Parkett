using System.Security.Cryptography;
using FluentAssertions;
using Parkett.Licensing;

namespace Parkett.Tests;

public class LicensingTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static (LicenseVerifier Verifier, ECDsa PrivateKey) CreatePair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        return (new LicenseVerifier(publicKey), key);
    }

    [Fact]
    public void Gueltiger_Schluessel_wird_akzeptiert()
    {
        var (verifier, key) = CreatePair();
        var license = new LicenseKey(Edition.Pro, "lars@example.org", Now.AddDays(-1), null);

        var result = verifier.Check(LicenseVerifier.Sign(license, key), Now);

        result.IsValid.Should().BeTrue();
        result.License!.Edition.Should().Be(Edition.Pro);
        result.License.LicensedTo.Should().Be("lars@example.org");
    }

    [Fact]
    public void Schluessel_eines_fremden_Signaturschluessels_wird_abgelehnt()
    {
        var (verifier, _) = CreatePair();
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var key = LicenseVerifier.Sign(new LicenseKey(Edition.Pro, "wer auch immer", Now, null), fremd);

        verifier.Check(key, Now).Status.Should().Be(LicenseStatus.SignatureInvalid);
    }

    [Fact]
    public void Manipulierte_Nutzdaten_werden_erkannt()
    {
        var (verifier, key) = CreatePair();
        var signed = LicenseVerifier.Sign(new LicenseKey(Edition.Free, "test", Now, null), key);

        // Erstes Zeichen der Nutzdaten kippen — Signatur passt dann nicht mehr.
        var payload = signed.Split('.')[0];
        var tampered = (payload[0] == 'A' ? 'B' : 'A') + payload[1..] + "." + signed.Split('.')[1];

        verifier.Check(tampered, Now).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Abgelaufene_Lizenz_wird_als_abgelaufen_gemeldet()
    {
        var (verifier, key) = CreatePair();
        var license = new LicenseKey(Edition.Pro, "test", Now.AddYears(-2), Now.AddDays(-1));

        verifier.Check(LicenseVerifier.Sign(license, key), Now).Status.Should().Be(LicenseStatus.Expired);
    }

    [Fact]
    public void Unsinniger_Text_ist_kein_Schluessel()
    {
        var (verifier, _) = CreatePair();

        verifier.Check("kein-schluessel", Now).Status.Should().Be(LicenseStatus.Malformed);
    }

    [Fact]
    public void Zeilenumbrueche_aus_kopierten_Mails_stoeren_nicht()
    {
        var (verifier, key) = CreatePair();
        var signed = LicenseVerifier.Sign(new LicenseKey(Edition.Full, "test", Now, null), key);

        var mitUmbruechen = signed[..20] + "\n  " + signed[20..];

        verifier.Check(mitUmbruechen, Now).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Ohne_Schluessel_bleibt_die_kostenlose_Fassung_aktiv()
    {
        var (verifier, _) = CreatePair();
        var provider = new LicenseKeyEditionProvider(verifier, storedKey: null, Now);

        provider.Current.Should().Be(Edition.Free);
    }

    [Fact]
    public void Ungueltiger_Schluessel_faellt_auf_die_kostenlose_Fassung_zurueck()
    {
        var (verifier, _) = CreatePair();
        var provider = new LicenseKeyEditionProvider(verifier, "murks", Now);

        provider.Current.Should().Be(Edition.Free);
        provider.SourceDescription.Should().Contain("nicht lesbar");
    }
}
