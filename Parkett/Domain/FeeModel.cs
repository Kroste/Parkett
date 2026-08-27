namespace Parkett.Domain;

/// <summary>
/// Gebührenmodell. Bewusst ein eigenes Konzept: Der Hauptlerneffekt eines Paper-Tradings
/// ist, dass Transaktionskosten kleine Konten auffressen. Ein Simulator ohne Gebühren
/// erzieht zu genau dem Verhalten, das im Echtbetrieb Geld kostet.
/// </summary>
public interface IFeeModel
{
    decimal CalculateFee(OrderSide side, decimal quantity, decimal price);
}

/// <summary>Gebühr = Grundbetrag + Prozentsatz vom Volumen, begrenzt durch Minimum/Maximum.</summary>
public sealed class TieredFeeModel : IFeeModel
{
    public decimal BaseFee { get; }

    public decimal PercentOfVolume { get; }

    public decimal MinimumFee { get; }

    public decimal? MaximumFee { get; }

    public TieredFeeModel(decimal baseFee, decimal percentOfVolume, decimal minimumFee = 0m, decimal? maximumFee = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseFee);
        ArgumentOutOfRangeException.ThrowIfNegative(percentOfVolume);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumFee);

        BaseFee = baseFee;
        PercentOfVolume = percentOfVolume;
        MinimumFee = minimumFee;
        MaximumFee = maximumFee;
    }

    /// <summary>Freier Handel (z. B. Einsteigermodus). Macht die Gebührenwirkung im Vergleich sichtbar.</summary>
    public static TieredFeeModel Free { get; } = new(0m, 0m);

    /// <summary>Typischer deutscher Neobroker: 1 € pro Order.</summary>
    public static TieredFeeModel Neobroker { get; } = new(1m, 0m);

    /// <summary>Typische Filialbank: 4,90 € Grundgebühr plus 0,25 % vom Volumen, mindestens 9,90 €.</summary>
    public static TieredFeeModel Hausbank { get; } = new(4.90m, 0.0025m, minimumFee: 9.90m, maximumFee: 59.90m);

    public decimal CalculateFee(OrderSide side, decimal quantity, decimal price)
    {
        var volume = Math.Abs(quantity * price);
        var fee = BaseFee + (volume * PercentOfVolume);

        if (fee < MinimumFee)
        {
            fee = MinimumFee;
        }

        if (MaximumFee is { } max && fee > max)
        {
            fee = max;
        }

        return Math.Round(fee, 2, MidpointRounding.AwayFromZero);
    }
}
