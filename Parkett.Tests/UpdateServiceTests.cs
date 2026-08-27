using FluentAssertions;
using Parkett.Services;

namespace Parkett.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("2.0.0-beta.1", "1.9.9", true)]
    public void Versionsvergleich_ist_semantisch(string kandidat, string aktuell, bool erwartet)
    {
        UpdateService.IsNewer(kandidat, aktuell).Should().Be(erwartet);
    }

    [Fact]
    public void Stringvergleich_Falle_wird_vermieden()
    {
        // "1.10.0" < "1.9.0" wäre die falsche Antwort eines reinen Stringvergleichs.
        UpdateService.IsNewer("1.10.0", "1.9.0").Should().BeTrue();
    }
}
