using Parkett.Simulation;

namespace Parkett.ViewModels;

/// <summary>
/// Anzeigbare Tempostufe. Nötig, weil eine ComboBox über dem nackten Enum "VeryFast"
/// anzeigen würde statt "16×".
/// </summary>
public sealed record SpeedOption(SimulationSpeed Value, string Label)
{
    public static SpeedOption For(SimulationSpeed speed) => new(speed, speed.Label());

    public override string ToString() => Label;
}
