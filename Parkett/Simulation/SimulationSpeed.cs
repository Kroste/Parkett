namespace Parkett.Simulation;

/// <summary>
/// Ablaufgeschwindigkeit der Sitzung. Bewusst diskrete Stufen statt eines Schiebereglers:
/// der Nutzer soll die Geschwindigkeit wiedererkennen, nicht einstellen.
/// </summary>
public enum SimulationSpeed
{
    Paused,
    Slow,
    Normal,
    Fast,
    VeryFast,
}

public static class SimulationSpeedExtensions
{
    /// <summary>Zeit zwischen zwei Kerzen. <see cref="SimulationSpeed.Paused"/> hat kein Intervall.</summary>
    public static TimeSpan? Interval(this SimulationSpeed speed) => speed switch
    {
        SimulationSpeed.Slow => TimeSpan.FromMilliseconds(1200),
        SimulationSpeed.Normal => TimeSpan.FromMilliseconds(500),
        SimulationSpeed.Fast => TimeSpan.FromMilliseconds(150),
        SimulationSpeed.VeryFast => TimeSpan.FromMilliseconds(40),
        _ => null,
    };

    public static string Label(this SimulationSpeed speed) => speed switch
    {
        SimulationSpeed.Slow => "0,5×",
        SimulationSpeed.Normal => "1×",
        SimulationSpeed.Fast => "4×",
        SimulationSpeed.VeryFast => "16×",
        _ => "Pause",
    };
}
