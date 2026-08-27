using FluentAssertions;
using Parkett.Domain;
using Parkett.Persistence;
using Parkett.Services;

namespace Parkett.Tests;

public class PersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Einstellungen_ueberleben_einen_Rundlauf()
    {
        using var dir = new TempDirectory();
        var service = new SettingsService(dir.Path, new TestProtector());

        service.Save(new AppSettings { LastSymbol = "SAP", DefaultQuantity = 25m });
        var geladen = service.Load();

        geladen.LastSymbol.Should().Be("SAP");
        geladen.DefaultQuantity.Should().Be(25m);
    }

    [Fact]
    public void Fehlende_Datei_liefert_Standardwerte()
    {
        using var dir = new TempDirectory();

        new SettingsService(dir.Path, new TestProtector()).Load().Should().Be(AppSettings.Default);
    }

    [Fact]
    public void Lizenzschluessel_liegt_verschluesselt_in_der_Datei()
    {
        using var dir = new TempDirectory();
        var service = new SettingsService(dir.Path, new TestProtector());

        service.Save(new AppSettings { LicenseKey = "GEHEIMER-SCHLUESSEL" });

        var rohesJson = File.ReadAllText(Path.Combine(dir.Path, "settings.json"));

        rohesJson.Should().NotContain("GEHEIMER-SCHLUESSEL");
        rohesJson.Should().Contain("ENC1:");
        service.Load().LicenseKey.Should().Be("GEHEIMER-SCHLUESSEL");
    }

    [Fact]
    public void Rest_der_Datei_bleibt_lesbares_Json()
    {
        // Genau das ist der Grund für Inline-Verschlüsselung: ein komplett verschlüsselter
        // Blob im Nutzerdatenordner sieht für Verhaltens-AV nach Ransomware aus.
        using var dir = new TempDirectory();
        var service = new SettingsService(dir.Path, new TestProtector());

        service.Save(new AppSettings { LastSymbol = "BMW", LicenseKey = "geheim" });

        File.ReadAllText(Path.Combine(dir.Path, "settings.json")).Should().Contain("BMW");
    }

    [Fact]
    public void Defekte_Datei_wird_gesichert_statt_ueberschrieben()
    {
        using var dir = new TempDirectory();
        var pfad = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(pfad, "{ das ist kein json");

        var geladen = new SettingsService(dir.Path, new TestProtector()).Load();

        geladen.Should().Be(AppSettings.Default);
        File.Exists(pfad + ".broken").Should().BeTrue("die kaputte Datei muss zur Rettung erhalten bleiben");
        File.ReadAllText(pfad + ".broken").Should().Contain("das ist kein json");
    }

    [Fact]
    public void Speichern_hinterlaesst_keine_temporaere_Datei()
    {
        using var dir = new TempDirectory();

        new SettingsService(dir.Path, new TestProtector()).Save(AppSettings.Default);

        Directory.EnumerateFiles(dir.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Sitzung_ueberlebt_Speichern_und_Laden()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);
        var session = new TradingSession(10_000m, TieredFeeModel.Neobroker);

        var quote = new Quote("SAP", 99m, 101m, 100m, Now, 1440);
        session.Submit(Order.Market("SAP", OrderSide.Buy, 10m, Now), quote, Now);

        store.Save(SessionSnapshotMapper.ToSnapshot(session, "SAP", 120, Now)).Should().BeTrue();

        var wiederhergestellt = SessionSnapshotMapper.ToSession(store.Load()!, TieredFeeModel.Neobroker);

        wiederhergestellt.Portfolio.Cash.Should().Be(session.Portfolio.Cash);
        wiederhergestellt.Portfolio.QuantityOf("SAP").Should().Be(10m);
        wiederhergestellt.Portfolio.TotalFees.Should().Be(1m);
        wiederhergestellt.Fills.Should().HaveCount(1);
    }

    [Fact]
    public void Gespeicherter_Kerzenindex_kommt_zurueck()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);
        var session = new TradingSession(5_000m, TieredFeeModel.Free);

        store.Save(SessionSnapshotMapper.ToSnapshot(session, "DEMO", 247, Now));

        store.Load()!.CandleIndex.Should().Be(247);
    }

    [Fact]
    public void Unbekannte_Formatversion_wird_ignoriert_statt_falsch_gelesen()
    {
        using var dir = new TempDirectory();
        var pfad = Path.Combine(dir.Path, "session.json");
        File.WriteAllText(pfad, """{ "version": 99, "symbol": "SAP", "candleIndex": 5, "startingCash": 1, "cash": 1 }""");

        new SessionStore(dir.Path).Load().Should().BeNull();
    }

    [Fact]
    public void Clear_verschiebt_statt_zu_loeschen()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);
        store.Save(SessionSnapshotMapper.ToSnapshot(new TradingSession(1_000m, TieredFeeModel.Free), "DEMO", 1, Now));

        store.Clear();

        // Move statt Delete: Verhaltens-AV wertet Löschketten als Wiper-Signatur.
        File.Exists(Path.Combine(dir.Path, "session.json")).Should().BeFalse();
        File.Exists(Path.Combine(dir.Path, "session.json.last")).Should().BeTrue();
    }

    [Fact]
    public void Kein_gespeicherter_Stand_meldet_sich_ehrlich()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);

        store.HasSavedSession.Should().BeFalse();
        store.Load().Should().BeNull();
        store.Clear();
    }

    [Fact]
    public void Positionen_und_Einstandskurse_bleiben_exakt()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);
        var session = new TradingSession(100_000m, TieredFeeModel.Free);
        var quote = new Quote("SAP", 123.45m, 123.55m, 123.50m, Now, 1440);

        session.Submit(Order.Market("SAP", OrderSide.Buy, 7m, Now), quote, Now);
        store.Save(SessionSnapshotMapper.ToSnapshot(session, "SAP", 10, Now));

        var zurueck = SessionSnapshotMapper.ToSession(store.Load()!, TieredFeeModel.Free);

        zurueck.Portfolio.GetPosition("SAP")!.AveragePrice.Should().Be(123.55m, "gekauft wird zum Briefkurs");
    }
}

public class SecretProtectorTests
{
    [Fact]
    public void Verschluesselter_Wert_kommt_unveraendert_zurueck()
    {
        using var dir = new TempDirectory();
        var protector = new SecretProtector(dir.Path);

        var geschuetzt = protector.Protect("mein-lizenzschluessel");

        geschuetzt.Should().StartWith("ENC1:");
        geschuetzt.Should().NotContain("mein-lizenzschluessel");
        protector.Unprotect(geschuetzt).Should().Be("mein-lizenzschluessel");
    }

    [Fact]
    public void Klartext_aus_Altbestaenden_wird_weiter_gelesen()
    {
        using var dir = new TempDirectory();

        new SecretProtector(dir.Path).Unprotect("alter-klartext").Should().Be("alter-klartext");
    }

    [Fact]
    public void Unlesbares_Chiffrat_wird_verworfen_statt_zu_werfen()
    {
        using var dir = new TempDirectory();

        new SecretProtector(dir.Path).Unprotect("ENC1:%%%kein-base64%%%").Should().BeNull();
    }

    [Fact]
    public void Leerer_Wert_bleibt_null()
    {
        using var dir = new TempDirectory();

        new SecretProtector(dir.Path).Unprotect(null).Should().BeNull();
        new SecretProtector(dir.Path).Unprotect("  ").Should().BeNull();
    }
}
