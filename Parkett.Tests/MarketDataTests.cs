using FluentAssertions;
using Parkett.Services;

namespace Parkett.Tests;

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
        new MarketDataLicense("Xetra", DataRedistributionRight.UserSuppliedCredentials, 15, "")
            .StatusText.Should().Be("Xetra · 15 Min. verzögert");

        new MarketDataLicense("Alpaca", DataRedistributionRight.UserSuppliedCredentials, 0, "")
            .StatusText.Should().Be("Alpaca · Echtzeit");
    }
}
