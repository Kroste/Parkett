using FluentAssertions;
using Parkett.Localization;
using Parkett.Services;

namespace Parkett.Tests;

[Collection("Localization")]
public class MarketDataTests
{
    [Fact]
    public void Csv_Zeile_wird_gelesen()
    {
        CsvHistoryProvider.TryParseCandle("2026-03-02,100.5,102.0,99.5,101.25,1234567", out var candle)
            .Should().BeTrue();

        candle.Open.Should().Be(100.5m);
        candle.Close.Should().Be(101.25m);
        candle.Volume.Should().Be(1_234_567L);
    }

    [Fact]
    public void Kopfzeile_und_Muell_werden_abgelehnt()
    {
        CsvHistoryProvider.TryParseCandle("Date,Open,High,Low,Close,Volume", out _).Should().BeFalse();
        CsvHistoryProvider.TryParseCandle("irgendwas", out _).Should().BeFalse();
        CsvHistoryProvider.TryParseCandle(string.Empty, out _).Should().BeFalse();
    }

    [Theory]
    // Genau die Zeilen, die scripts/fetch_history.py schreibt: ISO-Datum, Punkt als
    // Dezimaltrenner, abgeschnittene Nullen, ganzzahliges Volumen. Driftet das
    // Ausgabeformat des Skripts, bricht dieser Test statt einer stillen Fehlanzeige
    // im Chart. Gegenstück: scripts/test_fetch_history.py prüft dieselbe Zeile von
    // der anderen Seite.
    [InlineData("2026-01-05,88.4,88.51,87.35,87.81,703819", 88.4, 87.81, 703819L)]
    [InlineData("2026-05-07,109,109.8,107,107.49,1771740", 109, 107.49, 1771740L)]
    [InlineData("2026-03-02,100.5,102,99.5,101.25,1234567", 100.5, 101.25, 1234567L)]
    public void Ausgabe_des_Beschaffungsskripts_wird_gelesen(
        string zeile, decimal erwarteterOpen, decimal erwarteterClose, long erwartetesVolumen)
    {
        CsvHistoryProvider.TryParseCandle(zeile, out var candle).Should().BeTrue();

        candle.Open.Should().Be(erwarteterOpen);
        candle.Close.Should().Be(erwarteterClose);
        candle.Volume.Should().Be(erwartetesVolumen);
    }

    [Fact]
    public void Fehlendes_Volumen_ist_kein_Fehler()
    {
        CsvHistoryProvider.TryParseCandle("2026-03-02,10,11,9,10.5", out var candle).Should().BeTrue();
        candle.Volume.Should().Be(0L);
    }

    [Fact]
    public void Synthetischer_Spread_liegt_symmetrisch_um_den_Kurs()
    {
        var (bid, ask) = SyntheticSpread.Around(100m);

        bid.Should().BeLessThan(100m);
        ask.Should().BeGreaterThan(100m);
        (ask - 100m).Should().Be(100m - bid);
    }

    [Fact]
    public void Auslieferbare_Quellen_sind_im_Verkaufsbuild_nutzbar()
    {
        var frei = new MarketDataLicense("Historie", DataRedistributionRight.Redistributable, 1440, "");
        frei.IsUsableInPaidBuild.Should().BeTrue();
    }

    [Fact]
    public void Quelle_mit_Vertragspflicht_bleibt_ohne_Vertrag_gesperrt()
    {
        var ohneVertrag = new MarketDataLicense("Xetra", DataRedistributionRight.RequiresAgreement, 15, "");
        var mitVertrag = ohneVertrag with { AgreementReference = "MDDA-2026-0815" };

        ohneVertrag.IsUsableInPaidBuild.Should().BeFalse();
        mitVertrag.IsUsableInPaidBuild.Should().BeTrue();
    }

    [Fact]
    public void Statustext_nennt_die_Verzoegerung()
    {
        var vorher = LocalizationService.Instance.Current;

        try
        {
            LocalizationService.Instance.SetCulture("de");

            new MarketDataLicense("Xetra", DataRedistributionRight.UserSuppliedCredentials, 15, "")
                .StatusText.Should().Be("Xetra · 15 Min. verzögert");

            new MarketDataLicense("Alpaca", DataRedistributionRight.UserSuppliedCredentials, 0, "")
                .StatusText.Should().Be("Alpaca · Echtzeit");

            LocalizationService.Instance.SetCulture("en");

            new MarketDataLicense("Xetra", DataRedistributionRight.UserSuppliedCredentials, 15, "")
                .StatusText.Should().Be("Xetra · delayed 15 min");
        }
        finally
        {
            LocalizationService.Instance.Current = vorher;
        }
    }
}
