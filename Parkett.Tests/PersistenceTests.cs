using FluentAssertions;
using Parkett.Domain;
using Parkett.Persistence;
using Parkett.Services;

namespace Parkett.Tests;

public class PersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Der Fehler, der eine bezahlte Lizenz wertlos machte: Hauptfenster und
    /// Einstellungen halten je eine eigene Kopie der Einstellungen und schreiben die
    /// ganze Datei. Das Hauptfenster sicherte beim Beenden seine beim Start geladene
    /// Kopie — und setzte damit den Lizenzschlüssel zurück auf null, den das
    /// Einstellungsfenster kurz vorher gespeichert hatte. Sichtbar wurde das erst
    /// beim nächsten Start, als "kostenlose Fassung" dastand.
    /// </summary>
    [Fact]
    public void Update_behaelt_Felder_die_ein_anderes_Fenster_zwischenzeitlich_geschrieben_hat()
    {
        using var dir = new TempDirectory();
        var service = new SettingsService(dir.Path, new TestProtector());

        // Das Hauptfenster lädt beim Start — noch ohne Lizenz.
        var beimStart = service.Load();
        beimStart.LicenseKey.Should().BeNull();

        // Das Einstellungsfenster trägt einen Schlüssel ein und speichert.
        service.Update(s => s with { LicenseKey = "schluessel-aus-dem-einstellungsfenster" });

        // Das Hauptfenster sichert beim Beenden seinen eigenen Stand.
        service.Update(s => s with { LastSymbol = "SAP", DefaultQuantity = 42 });

        var nachNeustart = service.Load();

        nachNeustart.LicenseKey.Should().Be(
            "schluessel-aus-dem-einstellungsfenster",
            "sonst ist die Lizenz nach dem nächsten Start wieder weg");
        nachNeustart.LastSymbol.Should().Be("SAP");
        nachNeustart.DefaultQuantity.Should().Be(42);
    }

    /// <summary>Und die Gegenrichtung: das Einstellungsfenster darf den Sitzungsstand nicht plätten.</summary>
    [Fact]
    public void Update_aus_den_Einstellungen_laesst_den_Stand_des_Hauptfensters_stehen()
    {
        using var dir = new TempDirectory();
        var service = new SettingsService(dir.Path, new TestProtector());

        service.Update(s => s with { LastSymbol = "SAP", DefaultQuantity = 42 });
        service.Update(s => s with { UiCulture = "de", FeeModel = "Neobroker" });

        var geladen = service.Load();

        geladen.LastSymbol.Should().Be("SAP");
        geladen.DefaultQuantity.Should().Be(42);
        geladen.UiCulture.Should().Be("de");
        geladen.FeeModel.Should().Be("Neobroker");
    }

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
    public void Gesperrte_Datei_wird_nicht_in_Quarantaene_geschoben()
    {
        // Ein IO-Fehler heißt nicht "kaputter Inhalt": Virenscanner und Netzlaufwerke
        // sperren Dateien kurzzeitig. Ein .broken-Move würde hier gute Nutzerdaten
        // wegräumen — genau der Verlust, den die Quarantäne verhindern soll.
        //
        // FileShare.None sperrt nur unter Windows verbindlich; Linux kennt keine
        // Pflicht-Sperren, dort läse der zweite Zugriff die Datei einfach mit.
        // Lieber ehrlich überspringen als im CI eine Prüfung vortäuschen.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Dateisperren sind nur unter Windows verbindlich.");

        using var dir = new TempDirectory();
        var pfad = Path.Combine(dir.Path, "settings.json");
        new SettingsService(dir.Path, new TestProtector()).Save(new AppSettings { LastSymbol = "SAP" });

        // FileShare.Delete ist hier entscheidend, nicht FileShare.None: Lesen bleibt
        // blockiert, Umbenennen erlaubt. Mit None scheitert auch der .broken-Move,
        // und der Test wäre unter beiden Fassungen grün — er würde nichts beweisen.
        using (File.Open(pfad, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            new SettingsService(dir.Path, new TestProtector()).Load().Should().Be(AppSettings.Default);
        }

        File.Exists(pfad + ".broken").Should().BeFalse("die Datei war nur gesperrt, nicht defekt");
        new SettingsService(dir.Path, new TestProtector()).Load().LastSymbol.Should().Be("SAP");
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
    /// <summary>
    /// Ein Stand aus Version 1 kennt nur ein einziges <c>Symbol</c> und gar kein
    /// <c>Symbols</c>. Er muss weiter lesbar bleiben — sonst verliert jeder, der von
    /// einer älteren Fassung aktualisiert, seine unterbrochene Sitzung. Der Test
    /// schreibt bewusst rohes JSON im alten Format statt über den Mapper zu gehen:
    /// nur so prüft er wirklich das Format und nicht die aktuelle Klasse.
    /// </summary>
    [Fact]
    public void Ein_gespeicherter_Stand_aus_Version_1_bleibt_lesbar()
    {
        using var dir = new TempDirectory();

        var altesFormat = """
            {
              "version": 1,
              "symbol": "SAP",
              "candleIndex": 120,
              "startingCash": 10000,
              "cash": 8989,
              "currency": "EUR",
              "totalFees": 1,
              "realizedPnL": 0,
              "positions": [ { "symbol": "SAP", "quantity": 10, "averagePrice": 101 } ],
              "fills": [],
              "savedAt": "2026-05-04T10:00:00+00:00"
            }
            """;

        File.WriteAllText(Path.Combine(dir.Path, "session.json"), altesFormat);

        var geladen = new SessionStore(dir.Path).Load();

        geladen.Should().NotBeNull();
        geladen!.Symbol.Should().Be("SAP");
        geladen.CandleIndex.Should().Be(120);
        geladen.Symbols.Should().BeEmpty("Version 1 kannte die Liste noch nicht");

        // Und die Sitzung selbst kommt vollständig zurück.
        var sitzung = SessionSnapshotMapper.ToSession(geladen, TieredFeeModel.Neobroker);
        sitzung.Portfolio.Cash.Should().Be(8989m);
        sitzung.Portfolio.Positions.Should().ContainKey("SAP");
    }

    /// <summary>Neue Stände führen alle Instrumente mit — sonst ließen sich Positionen
    /// in anderen Werten nach dem Fortsetzen nicht bewerten.</summary>
    [Fact]
    public void Ein_Stand_merkt_sich_alle_Instrumente_der_Sitzung()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);
        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        store.Save(SessionSnapshotMapper.ToSnapshot(session, "SAP", 42, Now, ["SAP", "DEMO", "BMW"]));

        var geladen = store.Load()!;

        geladen.Symbol.Should().Be("SAP", "das war das angezeigte Instrument");
        geladen.Symbols.Should().BeEquivalentTo(["SAP", "DEMO", "BMW"]);
    }

    /// <summary>Ohne Liste gilt das angezeigte Instrument — der Einzelfall bleibt bequem.</summary>
    [Fact]
    public void Ohne_Instrumentenliste_steht_das_angezeigte_Instrument_allein()
    {
        using var dir = new TempDirectory();
        var store = new SessionStore(dir.Path);

        store.Save(SessionSnapshotMapper.ToSnapshot(
            new TradingSession(1_000m, TieredFeeModel.Free), "DEMO", 5, Now));

        store.Load()!.Symbols.Should().BeEquivalentTo(["DEMO"]);
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
