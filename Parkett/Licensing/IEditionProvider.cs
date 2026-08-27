namespace Parkett.Licensing;

/// <summary>Woher die aktuelle Ausbaustufe kommt (Steam-Entitlement, Lizenzschlüssel, Demo).</summary>
public interface IEditionProvider
{
    Edition Current { get; }

    string SourceDescription { get; }
}

/// <summary>Feste Stufe — für die Steam-Builds und für Tests.</summary>
public sealed class FixedEditionProvider(Edition edition, string sourceDescription) : IEditionProvider
{
    public Edition Current { get; } = edition;

    public string SourceDescription { get; } = sourceDescription;
}
