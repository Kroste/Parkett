using FluentAssertions;
using Parkett.Domain;
using Parkett.Services;
using Parkett.Simulation;

namespace Parkett.Tests;

/// <summary>
/// Uhr und Sitzung zusammen über mehrere Instrumente — der Weg, den das Hauptfenster
/// bei jedem Takt geht. Die Einzelteile sind je für sich getestet; hier geht es um
/// das, was erst im Zusammenspiel schiefgehen kann: dass ein Kurs des einen Werts
/// eine Order im anderen auslöst, oder dass das Depot nur den angezeigten Wert
/// bewertet.
/// </summary>
public class MultiInstrumentSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Historie mit festen Schlusskursen an den angegebenen Tagen.</summary>
    private static SymbolHistory History(string symbol, params (int Tag, decimal Close)[] punkte) =>
        new(symbol, punkte
            .Select(p => new Candle(
                Start.AddDays(p.Tag),
                p.Close,
                p.Close + 1m,
                p.Close - 1m,
                p.Close,
                1000))
            .ToList());

    /// <summary>Taktet die Uhr bis zum Ende und füttert jeden Kurs in die Sitzung.</summary>
    private static void RunToEnd(SimulationClock clock, TradingSession session)
    {
        while (clock.Advance() is { } step)
        {
            foreach (var quote in step.Quotes)
            {
                session.OnQuote(quote, step.At);
            }
        }
    }

    [Fact]
    public void Das_Depot_fuehrt_Positionen_in_mehreren_Werten()
    {
        var clock = new SimulationClock(
            [
                History("A", (0, 100m), (1, 110m), (2, 120m)),
                History("B", (0, 50m), (1, 55m), (2, 60m)),
            ],
            warmupCandles: 1);

        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        session.Submit(Order.Market("A", OrderSide.Buy, 10m, clock.At), clock.QuoteFor("A"), clock.At);
        session.Submit(Order.Market("B", OrderSide.Buy, 20m, clock.At), clock.QuoteFor("B"), clock.At);

        session.Portfolio.Positions.Should().ContainKeys("A", "B");

        RunToEnd(clock, session);

        // 10 × 120 + 20 × 60 = 2.400 an Wertpapieren, der Rest ist Kasse.
        var wertpapiere = (10m * 120m) + (20m * 60m);
        session.Equity.Should().BeApproximately(session.Portfolio.Cash + wertpapiere, 0.01m);
    }

    /// <summary>
    /// Der Fehler, der ohne Symbolprüfung entstünde: eine Limit-Order in B wird
    /// ausgeführt, weil A durch ihren Preis läuft. Bei Werten mit ähnlichem
    /// Kursniveau fiele das in einer Sitzung nicht einmal auf.
    /// </summary>
    [Fact]
    public void Ein_Kurs_des_einen_Werts_loest_keine_Order_im_anderen_aus()
    {
        var clock = new SimulationClock(
            [
                History("A", (0, 100m), (1, 40m), (2, 100m)),   // A fällt tief unter das Limit
                History("B", (0, 100m), (1, 100m), (2, 100m)),  // B bleibt oben
            ],
            warmupCandles: 1);

        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        // Kauflimit bei 50 auf B — nur B darf es auslösen, und B fällt nie so tief.
        session.Submit(Order.Limit("B", OrderSide.Buy, 10m, 50m, clock.At), clock.QuoteFor("B"), clock.At);
        session.OpenOrders.Should().ContainSingle();

        RunToEnd(clock, session);

        session.OpenOrders.Should().ContainSingle("A unter 50 darf die Order in B nicht auslösen");
        session.Fills.Should().BeEmpty();
        session.Portfolio.Positions.Should().NotContainKey("B");
    }

    [Fact]
    public void Eine_Order_wird_vom_eigenen_Kurs_ausgeloest()
    {
        var clock = new SimulationClock(
            [
                History("A", (0, 100m), (1, 100m), (2, 100m)),
                History("B", (0, 100m), (1, 40m), (2, 100m)),   // B fällt unter das Limit
            ],
            warmupCandles: 1);

        var session = new TradingSession(10_000m, TieredFeeModel.Free);
        session.Submit(Order.Limit("B", OrderSide.Buy, 10m, 50m, clock.At), clock.QuoteFor("B"), clock.At);

        RunToEnd(clock, session);

        session.OpenOrders.Should().BeEmpty();
        session.Fills.Should().ContainSingle().Which.Symbol.Should().Be("B");
        session.Portfolio.Positions.Should().ContainKey("B");
    }

    /// <summary>
    /// An einem Tag, an dem nur ein Wert handelt, darf das Depot nicht springen: der
    /// stillstehende Wert behält seinen letzten Kurs, statt aus der Bewertung zu fallen.
    /// </summary>
    [Fact]
    public void Ein_Feiertag_laesst_den_Depotwert_nicht_einbrechen()
    {
        var clock = new SimulationClock(
            [
                History("A", (0, 100m), (1, 100m), (2, 100m), (3, 100m)),
                History("B", (0, 200m), (1, 200m), (3, 200m)),  // Tag 2 fehlt bei B
            ],
            warmupCandles: 1);

        var session = new TradingSession(10_000m, TieredFeeModel.Free);
        session.Submit(Order.Market("B", OrderSide.Buy, 10m, clock.At), clock.QuoteFor("B"), clock.At);

        var vorFeiertag = session.Equity;

        // Schritt auf Tag 2: nur A handelt, B hat an diesem Tag keine Kerze.
        var step = clock.Advance()!;
        step.Quotes.Should().ContainSingle().Which.Symbol.Should().Be("A");

        foreach (var quote in step.Quotes)
        {
            session.OnQuote(quote, step.At);
        }

        session.Equity.Should().Be(vorFeiertag, "B steht still, verschwindet aber nicht aus dem Depot");
    }

    /// <summary>
    /// Die Equity-Kurve bekommt pro Zeitpunkt genau einen Punkt, auch wenn an ihm
    /// mehrere Kurse eintreffen. Sonst zählte ein Tag mit fünf Instrumenten fünffach
    /// und verzerrte jeden Drawdown.
    /// </summary>
    [Fact]
    public void Die_Equity_Kurve_bekommt_pro_Zeitpunkt_genau_einen_Punkt()
    {
        var clock = new SimulationClock(
            [
                History("A", (0, 100m), (1, 101m), (2, 102m)),
                History("B", (0, 50m), (1, 51m), (2, 52m)),
                History("C", (0, 20m), (1, 21m), (2, 22m)),
            ],
            warmupCandles: 1);

        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        RunToEnd(clock, session);

        session.EquityCurve.Select(p => p.At).Should().OnlyHaveUniqueItems();
        session.EquityCurve.Should().HaveCount(1, "von Index 1 bleibt genau ein Schritt auf Index 2");
    }
}
