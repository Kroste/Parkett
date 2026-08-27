using FluentAssertions;
using Parkett.Licensing;

namespace Parkett.Tests;

public class FeatureGateTests
{
    private static FeatureGate Gate(Edition edition) =>
        new(new FixedEditionProvider(edition, "Test"));

    [Theory]
    [InlineData(Edition.Free, false)]
    [InlineData(Edition.Full, true)]
    [InlineData(Edition.Pro, true)]
    public void Mehrere_Depots_ab_der_Vollversion(Edition edition, bool erwartet)
    {
        Gate(edition).IsEnabled(Feature.MultiplePortfolios).Should().Be(erwartet);
    }

    [Theory]
    [InlineData(Edition.Free, false)]
    [InlineData(Edition.Full, false)]
    [InlineData(Edition.Pro, true)]
    public void Eigene_Datenquellen_nur_in_Pro(Edition edition, bool erwartet)
    {
        Gate(edition).IsEnabled(Feature.CustomDataProviders).Should().Be(erwartet);
    }

    [Fact]
    public void Kostenlose_Fassung_ist_auf_zehn_Instrumente_begrenzt()
    {
        Gate(Edition.Free).InstrumentLimit.Should().Be(FeatureGate.FreeInstrumentLimit);
        Gate(Edition.Full).InstrumentLimit.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Hinweistext_nennt_die_noetige_Stufe()
    {
        Gate(Edition.Free).UpgradeHint(Feature.StrategyReport).Should().Contain("Pro");
        Gate(Edition.Pro).UpgradeHint(Feature.StrategyReport).Should().BeEmpty();
    }
}
