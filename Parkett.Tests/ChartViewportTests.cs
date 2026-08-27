using System.Globalization;
using FluentAssertions;
using Parkett.Charting;
using Parkett.Domain;

namespace Parkett.Tests;

public class ChartViewportTests
{
    private static readonly DateTimeOffset Day0 = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<Candle> Candles(params decimal[] closes)
    {
        return closes
            .Select((c, i) => new Candle(Day0.AddDays(i), c, c + 2m, c - 2m, c, 1000))
            .ToList();
    }

    [Fact]
    public void Hoehere_Kurse_liegen_weiter_oben()
    {
        var viewport = new ChartViewport(Candles(100m, 110m, 120m), 400d, 200d);

        viewport.Y(120m).Should().BeLessThan(viewport.Y(100m), "y wächst nach unten");
    }

    [Fact]
    public void Preisband_bekommt_Luft_nach_oben_und_unten()
    {
        var viewport = new ChartViewport(Candles(100m, 110m), 400d, 200d);

        viewport.MinPrice.Should().BeLessThan(98m, "die tiefste Kerze liegt bei 98");
        viewport.MaxPrice.Should().BeGreaterThan(112m, "die höchste Kerze liegt bei 112");
    }

    [Fact]
    public void Waagerechte_Historie_erzeugt_keine_Division_durch_null()
    {
        var flat = Enumerable.Range(0, 5)
            .Select(i => new Candle(Day0.AddDays(i), 50m, 50m, 50m, 50m, 0))
            .ToList();

        var viewport = new ChartViewport(flat, 400d, 200d);

        viewport.Y(50m).Should().BeInRange(0d, 200d);
        viewport.MaxPrice.Should().BeGreaterThan(viewport.MinPrice);
    }

    [Fact]
    public void Y_und_PriceAt_sind_zueinander_invers()
    {
        var viewport = new ChartViewport(Candles(100m, 130m, 90m), 400d, 300d);

        var y = viewport.Y(115m);
        viewport.PriceAt(y).Should().BeApproximately(115m, 0.01m);
    }

    [Fact]
    public void Nur_die_letzten_Kerzen_werden_gezeigt()
    {
        var many = Candles(Enumerable.Range(0, 500).Select(i => 100m + i).ToArray());
        var viewport = new ChartViewport(many, 800d, 400d, maxVisibleCandles: 120);

        viewport.Count.Should().Be(120);
        viewport.VisibleCandles[^1].Should().Be(many[^1], "der aktuelle Rand muss sichtbar bleiben");
    }

    [Fact]
    public void Kerzen_fuellen_die_Breite_gleichmaessig()
    {
        var viewport = new ChartViewport(Candles(1m, 2m, 3m, 4m), 400d, 200d);

        viewport.SlotWidth.Should().Be(100d);
        viewport.XCenter(0).Should().Be(50d);
        viewport.XCenter(3).Should().Be(350d);
        viewport.BodyWidth.Should().BeLessThan(viewport.SlotWidth, "zwischen den Kerzen bleibt Abstand");
    }

    [Fact]
    public void Kerzenkoerper_bleibt_auch_bei_sehr_vielen_Kerzen_sichtbar()
    {
        var many = Candles(Enumerable.Range(0, 400).Select(i => 100m + (i % 7)).ToArray());
        var viewport = new ChartViewport(many, 100d, 200d, maxVisibleCandles: 400);

        viewport.BodyWidth.Should().BeGreaterThanOrEqualTo(1d, "unter einem Pixel wäre die Kerze unsichtbar");
    }

    [Theory]
    [InlineData(0.9, 1)]
    [InlineData(1.4, 2)]
    [InlineData(3.0, 5)]
    [InlineData(7.0, 10)]
    [InlineData(23.0, 50)]
    [InlineData(0.03, 0.05)]
    public void Rasterschritt_wird_auf_runde_Werte_gezogen(decimal roh, decimal erwartet)
    {
        ChartViewport.NiceStep(roh).Should().Be(erwartet);
    }

    [Fact]
    public void Preislinien_liegen_im_sichtbaren_Band()
    {
        var viewport = new ChartViewport(Candles(100m, 140m, 120m), 400d, 300d);
        var lines = viewport.PriceGridLines();

        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(p => p >= viewport.MinPrice && p <= viewport.MaxPrice);
        lines.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Zeitbeschriftung_zeigt_auf_echte_Kerzen()
    {
        var candles = Candles(Enumerable.Range(0, 40).Select(i => 100m + i).ToArray());
        var viewport = new ChartViewport(candles, 600d, 300d);

        var labels = viewport.TimeGridLines(5);

        labels.Should().NotBeEmpty();
        labels.Should().OnlyContain(l => viewport.VisibleCandles[l.Index].OpenTime == l.At);
    }

    [Fact]
    public void Position_ausserhalb_der_Flaeche_trifft_keine_Kerze()
    {
        var viewport = new ChartViewport(Candles(1m, 2m, 3m), 300d, 200d);

        viewport.IndexAt(-5d).Should().BeNull();
        viewport.IndexAt(400d).Should().BeNull();
        viewport.IndexAt(150d).Should().Be(1);
    }

    [Theory]
    [InlineData("de-DE", "dd.MM.yy")]
    [InlineData("en-US", "M/d/yy")]
    [InlineData("en-GB", "dd/MM/yy")]
    public void Achsendatum_folgt_der_Kultur(string kultur, string erwartet)
    {
        ChartViewport.AxisDatePattern(CultureInfo.GetCultureInfo(kultur)).Should().Be(erwartet);
    }

    [Fact]
    public void Achsendatum_bleibt_zweistellig_im_Jahr()
    {
        ChartViewport.AxisDatePattern(CultureInfo.GetCultureInfo("de-DE"))
            .Should().NotContain("yyyy", "auf der Achse ist kein Platz für vierstellige Jahre");
    }

    [Fact]
    public void Leerer_Chart_stuerzt_nicht_ab()
    {
        var viewport = new ChartViewport([], 400d, 200d);

        viewport.Count.Should().Be(0);
        viewport.PriceGridLines().Should().BeEmpty();
        viewport.TimeGridLines().Should().BeEmpty();
        viewport.IndexAt(10d).Should().BeNull();
    }

    [Fact]
    public void Nullgrosse_Flaeche_wird_auf_einen_Pixel_angehoben()
    {
        var viewport = new ChartViewport(Candles(1m, 2m), 0d, 0d);

        viewport.Width.Should().Be(1d);
        viewport.Height.Should().Be(1d);
    }
}
