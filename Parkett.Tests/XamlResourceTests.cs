using System.Text.RegularExpressions;
using FluentAssertions;

namespace Parkett.Tests;

/// <summary>
/// Styles und Resource-Keys scheitern in Avalonia STILL: weder ein toter
/// <c>Classes="accent"</c>-Verweis noch ein fehlender <c>{DynamicResource XyzBrush}</c>
/// erzeugt einen Compile-Fehler — beides rendert einfach falsch. Bei einem
/// Paletten-Refactoring über 200 Referenzen reichen drei übersehene für drei
/// unsichtbare Elemente.
///
/// Diese Tests machen aus dem stillen Renderfehler einen roten Testlauf und
/// laufen bei jedem Commit, statt nur dann, wenn jemand an ein Prüf-Snippet denkt.
/// </summary>
public class XamlResourceTests
{
    /// <summary>Die Palette und die Style-Bibliothek — die einzige Datei, die Farben definieren darf.</summary>
    private const string PaletteFile = "App.axaml";

    [Fact]
    public void Jeder_referenzierte_Resource_Key_ist_auch_definiert()
    {
        var definiert = new HashSet<string>(StringComparer.Ordinal);
        var referenziert = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var datei in XamlDateien())
        {
            var text = File.ReadAllText(datei);

            foreach (Match treffer in Regex.Matches(text, @"x:Key=""([^""]+)"""))
            {
                definiert.Add(treffer.Groups[1].Value);
            }

            foreach (Match treffer in Regex.Matches(text, @"\{(?:Dynamic|Static)Resource\s+([^}\s]+)\}"))
            {
                referenziert[treffer.Groups[1].Value] = Path.GetFileName(datei);
            }
        }

        referenziert.Should().NotBeEmpty("sonst greift die Prüfung ins Leere");

        // Keys mit System-Präfix kommen aus dem Framework-Theme, nicht aus unserem XAML.
        var fehlend = referenziert
            .Where(r => !r.Key.StartsWith("System", StringComparison.Ordinal))
            .Where(r => !definiert.Contains(r.Key))
            .Select(r => $"{r.Key} (verwendet in {r.Value})")
            .ToList();

        fehlend.Should().BeEmpty("ein fehlender Key rendert still falsch statt den Build zu brechen");
    }

    [Fact]
    public void Jede_benutzte_Style_Klasse_hat_einen_Selektor()
    {
        var definiert = new HashSet<string>(StringComparer.Ordinal);
        var benutzt = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var datei in XamlDateien())
        {
            var text = File.ReadAllText(datei);

            foreach (Match treffer in Regex.Matches(text, @"Selector=""([^""]+)"""))
            {
                foreach (Match klasse in Regex.Matches(treffer.Groups[1].Value, @"\.([A-Za-z0-9_-]+)"))
                {
                    definiert.Add(klasse.Groups[1].Value);
                }
            }

            foreach (Match treffer in Regex.Matches(text, @"Classes=""([^""{]+)"""))
            {
                foreach (var klasse in treffer.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    benutzt[klasse] = Path.GetFileName(datei);
                }
            }
        }

        benutzt.Should().NotBeEmpty("sonst greift die Prüfung ins Leere");

        var tot = benutzt
            .Where(b => !definiert.Contains(b.Key))
            .Select(b => $"{b.Key} (verwendet in {b.Value})")
            .ToList();

        tot.Should().BeEmpty("eine Klasse ohne Style ist ein unsichtbarer Fehler — real in DTM zwei Releases lang");
    }

    [Fact]
    public void Farben_stehen_nur_in_der_Palette()
    {
        // Ein hartkodiertes #6B7280 im Fenster überlebt jeden Themewechsel und
        // fällt erst auf, wenn jemand die Palette anfasst.
        var suender = new List<string>();

        foreach (var datei in XamlDateien().Where(d => !Path.GetFileName(d).Equals(PaletteFile, StringComparison.Ordinal)))
        {
            foreach (Match treffer in Regex.Matches(File.ReadAllText(datei), @"""(#[0-9A-Fa-f]{3,8})"""))
            {
                suender.Add($"{treffer.Groups[1].Value} in {Path.GetFileName(datei)}");
            }
        }

        suender.Should().BeEmpty($"Farben gehören als DynamicResource-Key in {PaletteFile}");
    }

    [Fact]
    public void Kein_Resource_Key_ist_doppelt_vergeben()
    {
        foreach (var datei in XamlDateien())
        {
            var keys = Regex.Matches(File.ReadAllText(datei), @"x:Key=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .ToList();

            // Doppelte Keys werfen erst beim Laden des XAML — also beim Nutzer.
            keys.Should().OnlyHaveUniqueItems($"doppelte x:Key in {Path.GetFileName(datei)} werfen zur Laufzeit");
        }
    }

    private static IEnumerable<string> XamlDateien() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Parkett"), "*.axaml", SearchOption.AllDirectories);

    /// <summary>Vom Testausgabeverzeichnis nach oben, erkennbar an der Solution-Datei.</summary>
    private static string RepoRoot()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null)
        {
            if (File.Exists(Path.Combine(verzeichnis.FullName, "Parkett.slnx")))
            {
                return verzeichnis.FullName;
            }

            verzeichnis = verzeichnis.Parent;
        }

        throw new DirectoryNotFoundException("Parkett.slnx nicht gefunden — Repo-Wurzel nicht auflösbar.");
    }
}
