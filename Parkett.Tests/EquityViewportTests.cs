using FluentAssertions;
using Parkett.Charting;
using Parkett.Domain;

namespace Parkett.Tests;

public class EquityViewportTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<EquityPoint> Kurve(params decimal[] werte) =>
        werte.Select((w, i) => new EquityPoint(Start.AddDays(i), w)).ToList();

    [Fact]
    public void Startkapital_bleibt_sichtbar_auch_wenn_die_Kurve_nie_dorthin_zurueckkehrt()
    {
        // Ohne diese Regel zoomt die Kurve auf ihr eigenes Min/Max und ein Verlust
        // von 40 % sieht aus wie ein Gewinn von 40 %.
        var viewport = new EquityViewport(Kurve(9_000m, 8_400m, 6_000m), 10_000m, 400d, 200d);

        viewport.MinValue.Should().BeLessThan(6_000m);
        viewport.MaxValue.Should().BeGreaterThan(10_000m, "die Startlinie muss ins Bild passen");

        viewport.StartLineY.Should().BeInRange(0d, 200d);
    }

    [Fact]
    public void Hoehere_Werte_liegen_weiter_oben()
    {
        var viewport = new EquityViewport(Kurve(10_000m, 11_000m, 12_000m), 10_000m, 400d, 200d);

        // Bildschirmkoordinaten: 0 ist oben.
        viewport.Y(12_000m).Should().BeLessThan(viewport.Y(10_000m));
        viewport.Y(10_000m).Should().BeLessThan(viewport.Y(8_000m));
    }

    [Fact]
    public void Erster_Punkt_sitzt_links_letzter_rechts()
    {
        var viewport = new EquityViewport(Kurve(10_000m, 10_500m, 9_800m, 10_200m), 10_000m, 400d, 200d);

        viewport.X(0).Should().Be(0d);
        viewport.X(3).Should().Be(400d);
        viewport.X(1).Should().BeInRange(0d, 400d);
    }

    [Fact]
    public void Ein_einzelner_Punkt_kippt_die_Skalierung_nicht()
    {
        var viewport = new EquityViewport(Kurve(10_000m), 10_000m, 400d, 200d);

        viewport.X(0).Should().Be(0d);
        viewport.Y(10_000m).Should().BeInRange(0d, 200d);
        viewport.ValueGridLines().Should().NotBeNull();
    }

    [Fact]
    public void Leere_Kurve_liefert_eine_brauchbare_Spanne()
    {
        var viewport = new EquityViewport([], 10_000m, 400d, 200d);

        viewport.Count.Should().Be(0);
        viewport.MaxValue.Should().BeGreaterThan(viewport.MinValue, "sonst teilt das Zeichnen durch null");
        viewport.StartLineY.Should().BeInRange(0d, 200d);
    }

    [Fact]
    public void Sitzung_ohne_jede_Bewegung_erzeugt_trotzdem_eine_Spanne()
    {
        var viewport = new EquityViewport(Kurve(10_000m, 10_000m, 10_000m), 10_000m, 400d, 200d);

        viewport.MaxValue.Should().BeGreaterThan(viewport.MinValue);
        viewport.Y(10_000m).Should().BeInRange(0d, 200d);
    }

    [Fact]
    public void Rasterlinien_liegen_auf_runden_Werten_und_im_Bild()
    {
        var viewport = new EquityViewport(Kurve(10_000m, 11_300m, 9_400m), 10_000m, 400d, 200d);

        var linien = viewport.ValueGridLines();

        linien.Should().NotBeEmpty();
        linien.Should().OnlyContain(l => l >= viewport.MinValue && l <= viewport.MaxValue);
    }

    [Fact]
    public void Groesster_Rueckgang_wird_am_Tiefpunkt_nach_dem_Hoch_markiert()
    {
        // Hoch bei 12.000 (Index 2), Tief bei 9.000 (Index 4) — der Rückgang misst
        // vom Höchststand, nicht vom Start.
        var viewport = new EquityViewport(
            Kurve(10_000m, 11_000m, 12_000m, 10_500m, 9_000m, 9_500m), 10_000m, 400d, 200d);

        viewport.MaxDrawdownIndex().Should().Be(4);
    }

    [Fact]
    public void Ohne_Rueckgang_gibt_es_keine_Markierung()
    {
        var viewport = new EquityViewport(Kurve(10_000m, 10_400m, 11_000m), 10_000m, 400d, 200d);

        viewport.MaxDrawdownIndex().Should().BeNull();
    }

    [Fact]
    public void Markierung_passt_zur_Kennzahl_des_Berichts()
    {
        // Beide Wege müssen denselben Tiefpunkt meinen, sonst zeigt der Bericht
        // eine Zahl und der Chart einen anderen Punkt.
        var kurve = Kurve(10_000m, 12_000m, 8_400m, 9_000m);
        var viewport = new EquityViewport(kurve, 10_000m, 400d, 200d);

        var index = viewport.MaxDrawdownIndex();
        index.Should().NotBeNull();

        var amTiefpunkt = (12_000m - kurve[index!.Value].Equity) / 12_000m * 100m;

        amTiefpunkt.Should().Be(PerformanceCalculator.CalculateMaxDrawdownPercent(kurve));
    }
}
