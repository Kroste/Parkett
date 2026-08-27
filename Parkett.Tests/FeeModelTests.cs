using FluentAssertions;
using Parkett.Domain;

namespace Parkett.Tests;

public class FeeModelTests
{
    [Fact]
    public void Absichtlich_kaputt_zum_Pruefen_der_Annotation()
    {
        TieredFeeModel.Neobroker.CalculateFee(OrderSide.Buy, 1m, 100m).Should().Be(999m);
    }

    [Fact]
    public void Neobroker_nimmt_einen_Euro_unabhaengig_vom_Volumen()
    {
        TieredFeeModel.Neobroker.CalculateFee(OrderSide.Buy, 1m, 50m).Should().Be(1m);
        TieredFeeModel.Neobroker.CalculateFee(OrderSide.Buy, 500m, 200m).Should().Be(1m);
    }

    [Fact]
    public void Hausbank_wendet_Mindestgebuehr_an()
    {
        // 4,90 + 0,25 % von 500 = 6,15 → Mindestgebühr 9,90 greift
        TieredFeeModel.Hausbank.CalculateFee(OrderSide.Buy, 5m, 100m).Should().Be(9.90m);
    }

    [Fact]
    public void Hausbank_deckelt_bei_der_Maximalgebuehr()
    {
        TieredFeeModel.Hausbank.CalculateFee(OrderSide.Buy, 1_000m, 500m).Should().Be(59.90m);
    }

    [Fact]
    public void Freier_Handel_kostet_nichts()
    {
        TieredFeeModel.Free.CalculateFee(OrderSide.Sell, 100m, 42m).Should().Be(0m);
    }

    [Theory]
    [InlineData(500, 20.8)]
    [InlineData(1000, 10.4)]
    [InlineData(5000, 2.1)]
    public void Gebuehrenlast_kleiner_Konten_ist_dramatisch(decimal kapital, decimal erwarteteLastProzent)
    {
        // Ein Roundtrip pro Woche beim Neobroker: 52 × 2 Orders × 1 €
        var jahresgebuehr = 52m * 2m * TieredFeeModel.Neobroker.CalculateFee(OrderSide.Buy, 1m, 100m);
        var lastProzent = Math.Round(jahresgebuehr / kapital * 100m, 1);

        lastProzent.Should().Be(erwarteteLastProzent);
    }
}
