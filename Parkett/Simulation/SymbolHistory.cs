using Parkett.Domain;

namespace Parkett.Simulation;

/// <summary>Die Historie eines Instruments, wie sie in eine Sitzung eingeht.</summary>
public sealed record SymbolHistory(string Symbol, IReadOnlyList<Candle> Candles)
{
    public string Symbol { get; } = string.IsNullOrWhiteSpace(Symbol)
        ? throw new ArgumentException("Ein Instrument braucht ein Symbol.", nameof(Symbol))
        : Symbol;

    public IReadOnlyList<Candle> Candles { get; } =
        Candles ?? throw new ArgumentNullException(nameof(Candles));
}
