using FluentAssertions;
using NLog;
using NLog.Layouts;
using Parkett.Services;

namespace Parkett.Tests;

public class MaskingTests
{
    [Theory]
    [InlineData("api_key=abc123", "api_key=***")]
    [InlineData("Token: sehr-geheim", "Token=***")]
    [InlineData("Passwort=hunter2", "Passwort=***")]
    [InlineData("Server=db1;Password=geheim;Database=x", "Server=db1;Password=***;Database=x")]
    public void Geheimnisse_werden_maskiert(string eingabe, string erwartet)
    {
        MaskingLayoutRenderer.Mask(eingabe).Should().Be(erwartet);
    }

    [Fact]
    public void Lizenzschluessel_landet_nie_vollstaendig_im_Log()
    {
        var key = new string('A', 24) + "." + new string('B', 64);

        MaskingLayoutRenderer.Mask($"Lizenz geprüft: {key}").Should().NotContain(key);
    }

    [Fact]
    public void Harmloser_Text_bleibt_unveraendert()
    {
        const string text = "Order ausgeführt: Kauf 10 SAP zu 101,25";

        MaskingLayoutRenderer.Mask(text).Should().Be(text);
    }

    [Fact]
    public void Layout_rendert_die_vollstaendige_Nachricht()
    {
        // Regressionstest: ist der masked-Renderer nicht registriert, liefert NLog
        // für diese Layout-Zeile nur noch "}" statt der Nachricht.
        var layout = Layout.FromString("${masked:inner=${message}}");
        var ereignis = LogEventInfo.Create(LogLevel.Info, "test", "Handelssitzung eröffnet: 10000 EUR");

        layout.Render(ereignis).Should().Be("Handelssitzung eröffnet: 10000 EUR");
    }
}
