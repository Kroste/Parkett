using FluentAssertions;
using Parkett.Domain;
using Parkett.Simulation;

namespace Parkett.Tests;

public class SimulationClockTests
{
    private static IReadOnlyList<Candle> History(int count, decimal start = 100m)
    {
        var candles = new List<Candle>();
        var day = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < count; i++)
        {
            var close = start + i;
            candles.Add(new Candle(day.AddDays(i), close - 0.5m, close + 1m, close - 1m, close, 1000));
        }

        return candles;
    }

    [Fact]
    public void Vorlauf_deckt_erste_Kerzen_sofort_auf()
    {
        var clock = new SimulationClock("DEMO", History(200), warmupCandles: 60);

        clock.Index.Should().Be(60);
        clock.Visible.Should().HaveCount(61, "der Vorlauf umfasst Index 0 bis einschließlich 60");
    }

    [Fact]
    public void Vorlauf_wird_auf_die_Historienlaenge_begrenzt()
    {
        var clock = new SimulationClock("DEMO", History(10), warmupCandles: 500);

        clock.Index.Should().Be(9);
        clock.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void Zu_kurze_Historie_wird_abgelehnt()
    {
        var act = () => new SimulationClock("DEMO", History(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Advance_deckt_genau_eine_Kerze_auf()
    {
        var clock = new SimulationClock("DEMO", History(100), warmupCandles: 10);
        var vorher = clock.Visible.Count;

        var step = clock.Advance();

        step.Should().NotBeNull();
        clock.Visible.Should().HaveCount(vorher + 1);
        step!.At.Should().Be(clock.Current.OpenTime);
        step.Quotes.Should().ContainSingle().Which.Last.Should().Be(clock.Current.Close);
    }

    [Fact]
    public void Advance_am_Ende_liefert_null_statt_stillzustehen()
    {
        var clock = new SimulationClock("DEMO", History(12), warmupCandles: 10);

        clock.Advance().Should().NotBeNull();
        clock.IsFinished.Should().BeTrue();
        clock.Advance().Should().BeNull();
    }

    [Fact]
    public void Sichtbare_Kerzen_enthalten_nie_die_Zukunft()
    {
        var history = History(100);
        var clock = new SimulationClock("DEMO", history, warmupCandles: 30);

        clock.Advance();

        // Genau das ist der Kern eines fairen Simulators: der Chart darf nichts zeigen,
        // was der Spieler zum Entscheidungszeitpunkt nicht wissen konnte.
        clock.Visible.Should().OnlyContain(c => c.OpenTime <= clock.Current.OpenTime);
        clock.Visible.Last().Should().Be(history[31]);
    }

    [Fact]
    public void Kurs_der_aktuellen_Kerze_hat_Geld_unter_und_Brief_ueber_dem_Schluss()
    {
        var clock = new SimulationClock("DEMO", History(50), warmupCandles: 10);
        var quote = clock.CurrentQuote;

        quote.Bid.Should().BeLessThan(clock.Current.Close);
        quote.Ask.Should().BeGreaterThan(clock.Current.Close);
        quote.Symbol.Should().Be("DEMO");
    }

    [Fact]
    public void Fortschritt_laeuft_von_Vorlauf_bis_eins()
    {
        var clock = new SimulationClock("DEMO", History(11), warmupCandles: 9);

        var step = clock.Advance()!;
        step.Progress.Should().BeApproximately(1d, 0.001d);
        step.IsLast.Should().BeTrue();
    }

    [Fact]
    public void Reset_startet_dieselbe_Historie_neu()
    {
        var clock = new SimulationClock("DEMO", History(100), warmupCandles: 20);

        while (clock.Advance() is not null)
        {
        }

        clock.Reset(20);

        clock.Index.Should().Be(20);
        clock.IsFinished.Should().BeFalse();
    }

    [Theory]
    [InlineData(SimulationSpeed.Paused, false)]
    [InlineData(SimulationSpeed.Slow, true)]
    [InlineData(SimulationSpeed.VeryFast, true)]
    public void Nur_laufende_Stufen_haben_ein_Intervall(SimulationSpeed speed, bool hatIntervall)
    {
        speed.Interval().HasValue.Should().Be(hatIntervall);
    }

    [Fact]
    public void Hoehere_Stufe_bedeutet_kuerzeres_Intervall()
    {
        SimulationSpeed.Fast.Interval()!.Value.Should().BeLessThan(SimulationSpeed.Normal.Interval()!.Value);
        SimulationSpeed.VeryFast.Interval()!.Value.Should().BeLessThan(SimulationSpeed.Fast.Interval()!.Value);
    }

    // ---- Mehrere Instrumente auf einer gemeinsamen Zeitachse ----

    /// <summary>Aus Tagesnummern eine Historie bauen — so lassen sich Lücken gezielt setzen.</summary>
    private static IReadOnlyList<Candle> HistoryAt(params int[] tage)
    {
        var start = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

        return tage.Select(tag =>
        {
            var close = 100m + tag;
            return new Candle(start.AddDays(tag), close - 0.5m, close + 1m, close - 1m, close, 1000);
        }).ToList();
    }

    [Fact]
    public void Die_Zeitachse_ist_die_Vereinigung_aller_Handelstage()
    {
        var clock = new SimulationClock(
            [
                new SymbolHistory("A", HistoryAt(0, 1, 2, 3)),
                new SymbolHistory("B", HistoryAt(0, 2, 4)),
            ],
            warmupCandles: 1);

        // 0,1,2,3 aus A plus 4 aus B — jeder Tag genau einmal.
        clock.Total.Should().Be(5);
    }

    /// <summary>
    /// Der Grund für die gemeinsame Zeitachse: ein Instrument, das an einem Tag nicht
    /// gehandelt wurde, darf die anderen nicht aufhalten und nicht vorlaufen.
    /// </summary>
    [Fact]
    public void Ein_Feiertag_bei_einem_Instrument_haelt_die_anderen_nicht_auf()
    {
        var clock = new SimulationClock(
            [
                new SymbolHistory("A", HistoryAt(0, 1, 2)),
                new SymbolHistory("B", HistoryAt(0, 2)),   // Tag 1 fehlt
            ],
            warmupCandles: 1);

        // Steht auf Tag 1: A hat eine neue Kerze, B nicht.
        clock.At.Should().Be(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero));
        clock.VisibleFor("A").Should().HaveCount(2);
        clock.VisibleFor("B").Should().HaveCount(1, "B wurde an diesem Tag nicht gehandelt");

        // B behält seinen letzten Kurs, statt zu fehlen oder auf 0 zu fallen.
        clock.QuoteFor("B").Last.Should().Be(100m);

        var step = clock.Advance();

        step!.Quotes.Should().HaveCount(2, "auf Tag 2 haben beide wieder eine Kerze");
        clock.VisibleFor("B").Should().HaveCount(2);
    }

    [Fact]
    public void Ein_Schritt_liefert_nur_die_Instrumente_mit_neuer_Kerze()
    {
        var clock = new SimulationClock(
            [
                new SymbolHistory("A", HistoryAt(0, 1, 2)),
                new SymbolHistory("B", HistoryAt(0, 2)),
            ],
            warmupCandles: 1);

        clock.Reset(warmupCandles: 1);
        clock.Index.Should().Be(1);

        // Von Tag 1 auf Tag 2: beide handeln.
        clock.Advance()!.Quotes.Select(q => q.Symbol).Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public void Der_Vorlauf_deckt_bei_allen_Instrumenten_gleich_weit_auf()
    {
        var clock = new SimulationClock(
            [
                new SymbolHistory("A", HistoryAt(0, 1, 2, 3, 4)),
                new SymbolHistory("B", HistoryAt(0, 1, 2, 3, 4)),
            ],
            warmupCandles: 2);

        clock.VisibleFor("A").Should().HaveCount(3);
        clock.VisibleFor("B").Should().HaveCount(3);
    }

    /// <summary>Das angezeigte Instrument steuert nur die Anzeige, nie den Ablauf.</summary>
    [Fact]
    public void Umschalten_des_Charts_aendert_den_Ablauf_nicht()
    {
        var clock = new SimulationClock(
            [
                new SymbolHistory("A", HistoryAt(0, 1, 2)),
                new SymbolHistory("B", HistoryAt(0, 1, 2)),
            ],
            warmupCandles: 1);

        clock.ActiveSymbol.Should().Be("A", "das erste Instrument wird zuerst gezeigt");
        var indexVorher = clock.Index;

        clock.ShowSymbol("B").Should().BeTrue();

        clock.ActiveSymbol.Should().Be("B");
        clock.Index.Should().Be(indexVorher);
        clock.CurrentQuote.Symbol.Should().Be("B");
    }

    [Fact]
    public void Ein_unbekanntes_Instrument_wird_nicht_angezeigt()
    {
        var clock = new SimulationClock("DEMO", History(10), warmupCandles: 2);

        clock.ShowSymbol("GIBTESNICHT").Should().BeFalse();
        clock.ActiveSymbol.Should().Be("DEMO", "die Anzeige bleibt, wo sie war");
    }

    [Fact]
    public void Ohne_Instrument_gibt_es_keine_Sitzung()
    {
        var act = () => new SimulationClock([]);

        act.Should().Throw<ArgumentException>();
    }
}
