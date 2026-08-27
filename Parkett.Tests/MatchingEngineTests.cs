using FluentAssertions;
using Parkett.Domain;

namespace Parkett.Tests;

public class MatchingEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static Quote QuoteAt(decimal bid, decimal ask, string symbol = "SAP") =>
        new(symbol, bid, ask, (bid + ask) / 2m, Now, 15);

    private static MatchingEngine Engine(decimal fee = 0m) =>
        new(new TieredFeeModel(fee, 0m));

    [Fact]
    public void Marktkauf_wird_zum_Briefkurs_ausgefuehrt()
    {
        var result = Engine().TryExecute(
            Order.Market("SAP", OrderSide.Buy, 10m, Now),
            QuoteAt(99m, 101m),
            Portfolio.Open(10_000m),
            Now);

        result.Fill!.Price.Should().Be(101m, "Käufe zahlen den Brief, nicht die Mitte");
    }

    [Fact]
    public void Marktverkauf_wird_zum_Geldkurs_ausgefuehrt()
    {
        var portfolio = Portfolio.Open(10_000m)
            .Apply(new Fill(Guid.NewGuid(), "SAP", OrderSide.Buy, 10m, 100m, 0m, Now));

        var result = Engine().TryExecute(
            Order.Market("SAP", OrderSide.Sell, 10m, Now),
            QuoteAt(99m, 101m),
            portfolio,
            Now);

        result.Fill!.Price.Should().Be(99m);
    }

    [Fact]
    public void Kauf_ohne_ausreichende_Deckung_wird_abgelehnt()
    {
        var result = Engine().TryExecute(
            Order.Market("SAP", OrderSide.Buy, 1_000m, Now),
            QuoteAt(99m, 101m),
            Portfolio.Open(500m),
            Now);

        result.IsFilled.Should().BeFalse();
        result.Order.Status.Should().Be(OrderStatus.Rejected);
        result.Order.RejectReason.Should().Contain("Kapital");
    }

    [Fact]
    public void Leerverkauf_wird_abgelehnt()
    {
        var result = Engine().TryExecute(
            Order.Market("SAP", OrderSide.Sell, 5m, Now),
            QuoteAt(99m, 101m),
            Portfolio.Open(10_000m),
            Now);

        result.Order.Status.Should().Be(OrderStatus.Rejected);
        result.Order.RejectReason.Should().Contain("Leerverkauf");
    }

    [Fact]
    public void Kauflimit_bleibt_offen_solange_der_Brief_darueber_liegt()
    {
        var result = Engine().TryExecute(
            Order.Limit("SAP", OrderSide.Buy, 10m, limitPrice: 95m, Now),
            QuoteAt(99m, 101m),
            Portfolio.Open(10_000m),
            Now);

        result.IsFilled.Should().BeFalse();
        result.RemainsOpen.Should().BeTrue();
    }

    [Fact]
    public void Kauflimit_wird_ausgefuehrt_sobald_der_Brief_faellt()
    {
        var result = Engine().TryExecute(
            Order.Limit("SAP", OrderSide.Buy, 10m, limitPrice: 95m, Now),
            QuoteAt(93m, 94m),
            Portfolio.Open(10_000m),
            Now);

        result.Fill!.Price.Should().Be(94m);
    }

    [Fact]
    public void StopLoss_loest_erst_unterhalb_des_Stopkurses_aus()
    {
        var portfolio = Portfolio.Open(10_000m)
            .Apply(new Fill(Guid.NewGuid(), "SAP", OrderSide.Buy, 10m, 100m, 0m, Now));

        var order = Order.Stop("SAP", OrderSide.Sell, 10m, stopPrice: 90m, Now);
        var engine = Engine();

        engine.TryExecute(order, QuoteAt(95m, 96m), portfolio, Now).RemainsOpen.Should().BeTrue();
        engine.TryExecute(order, QuoteAt(89m, 90m), portfolio, Now).IsFilled.Should().BeTrue();
    }

    [Fact]
    public void Order_mit_null_Stueck_wird_abgelehnt()
    {
        var result = Engine().TryExecute(
            Order.Market("SAP", OrderSide.Buy, 0m, Now),
            QuoteAt(99m, 101m),
            Portfolio.Open(10_000m),
            Now);

        result.Order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public void Kurs_eines_fremden_Symbols_fuehrt_nicht_aus()
    {
        var result = Engine().TryExecute(
            Order.Market("SAP", OrderSide.Buy, 1m, Now),
            QuoteAt(99m, 101m, symbol: "BMW"),
            Portfolio.Open(10_000m),
            Now);

        result.Order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public void Gebuehr_wird_beim_Deckungscheck_mitgerechnet()
    {
        // 9 Stück zu 101 = 909; mit 100 € Gebühr sind es 1.009 > 1.000 verfügbar.
        var engine = new MatchingEngine(new TieredFeeModel(100m, 0m));

        var result = engine.TryExecute(
            Order.Market("SAP", OrderSide.Buy, 9m, Now),
            QuoteAt(99m, 101m),
            Portfolio.Open(1_000m),
            Now);

        result.Order.Status.Should().Be(OrderStatus.Rejected);
    }
}
