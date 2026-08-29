using FluentAssertions;
using Parkett.Domain;
using Parkett.Localization;
using Parkett.Services;
using Parkett.ViewModels;

namespace Parkett.Tests;

/// <summary>
/// Der Bericht-Export besteht aus zwei Teilen: dem Rendern (braucht ein Fenster und
/// wird über <c>--report-preview</c> geprüft) und dem Dateinamen samt Rückmeldung.
/// Der zweite Teil steht hier — er ist der, an dem sich unbemerkt etwas verschiebt.
/// </summary>
[Collection("Localization")]
public class ReportExportTests : IDisposable
{
    private static readonly DateTimeOffset Ende = new(2026, 2, 23, 17, 30, 0, TimeSpan.Zero);

    private readonly System.Globalization.CultureInfo _original = LocalizationService.Instance.Current;

    public void Dispose() => LocalizationService.Instance.Current = _original;

    [Fact]
    public void Dateiname_traegt_Symbol_und_Sitzungsende()
    {
        ReportExport.SuggestFileName("DEMO", Ende).Should().Be("Parkett-DEMO-2026-02-23.png");
    }

    /// <summary>
    /// Das Datum ist bewusst ISO und nicht kulturabhängig: mehrere Berichte in einem
    /// Ordner sollen chronologisch sortieren, und "23.02.2026" tut das nicht.
    /// </summary>
    [Fact]
    public void Datum_bleibt_ISO_auch_unter_deutscher_Kultur()
    {
        LocalizationService.Instance.SetCulture("de");

        ReportExport.SuggestFileName("DEMO", Ende).Should().Contain("2026-02-23");
    }

    /// <summary>
    /// Broker-Exporte tragen im Symbol gern einen Schrägstrich. Unter Linux wäre das
    /// ein Verzeichniswechsel, unter Windows ein ungültiger Name — beides schlägt
    /// erst im Speichern-Dialog fehl, wo niemand mehr damit rechnet.
    /// </summary>
    [Theory]
    [InlineData("DE0007236101/SIE.DE", "Parkett-DE0007236101SIE-DE-2026-02-23.png")]
    [InlineData("BRK.B", "Parkett-BRK-B-2026-02-23.png")]
    [InlineData("A B", "Parkett-AB-2026-02-23.png")]
    [InlineData("../../etc/passwd", "Parkett-etcpasswd-2026-02-23.png")]
    public void Unbrauchbare_Zeichen_im_Symbol_fallen_weg(string symbol, string erwartet)
    {
        ReportExport.SuggestFileName(symbol, Ende).Should().Be(erwartet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void Ohne_brauchbares_Symbol_greift_ein_Ersatzname(string? symbol)
    {
        ReportExport.SuggestFileName(symbol, Ende).Should().Be("Parkett-Sitzung-2026-02-23.png");
    }

    [Fact]
    public void Das_Berichtsfenster_schlaegt_den_Namen_aus_dem_Sitzungsende_vor()
    {
        Bericht().SuggestedFileName.Should().Be("Parkett-DEMO-2026-02-23.png");
    }

    /// <summary>Ohne Pfad in der Meldung sucht der Nutzer die Datei.</summary>
    [Fact]
    public void Die_Erfolgsmeldung_nennt_das_Ziel()
    {
        LocalizationService.Instance.SetCulture("de");
        var vm = Bericht();

        vm.HasExportStatus.Should().BeFalse("vor dem ersten Export steht dort nichts");

        vm.ReportExportSucceeded("/home/lars/Parkett-DEMO-2026-02-23.png");

        vm.HasExportStatus.Should().BeTrue();
        vm.ExportStatusText.Should().Be("Gespeichert als /home/lars/Parkett-DEMO-2026-02-23.png");
    }

    [Fact]
    public void Ein_Fehler_landet_in_der_Statuszeile_statt_im_Nichts()
    {
        LocalizationService.Instance.SetCulture("de");
        var vm = Bericht();

        vm.ReportExportFailed("Zugriff verweigert");

        vm.ExportStatusText.Should().Contain("Zugriff verweigert");
    }

    /// <summary>
    /// Dieselbe Regel wie im Hauptfenster: die Meldung ist als Schlüssel plus
    /// Argumente gemerkt. Wäre sie ein fertiger String, bliebe sie beim Sprachwechsel
    /// in der alten Sprache stehen.
    /// </summary>
    [Fact]
    public void Die_Statusmeldung_folgt_dem_Sprachwechsel()
    {
        LocalizationService.Instance.SetCulture("de");
        var vm = Bericht();
        vm.ReportExportSucceeded("bericht.png");

        vm.ExportStatusText.Should().StartWith("Gespeichert");

        LocalizationService.Instance.SetCulture("en");

        vm.ExportStatusText.Should().StartWith("Saved");
        vm.ExportStatusText.Should().Contain("bericht.png");
    }

    private static ReportWindowViewModel Bericht()
    {
        var report = new PerformanceReport(
            StartEquity: 10_000m,
            EndEquity: 9_850m,
            TotalReturnPercent: -1.5m,
            MaxDrawdownPercent: 8.65m,
            TotalFees: 620m,
            TradeCount: 31,
            WinRatePercent: 45.16m);

        return new ReportWindowViewModel(
            report,
            [new EquityPoint(Ende.AddDays(-49), 10_000m), new EquityPoint(Ende, 9_850m)],
            "DEMO",
            49,
            10_000m);
    }
}
