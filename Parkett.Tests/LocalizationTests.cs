using System.Globalization;
using FluentAssertions;
using Parkett.Localization;

namespace Parkett.Tests;

/// <summary>
/// Diese Tests laufen gegen den echten ResourceManager. Sie fangen genau den Fehler,
/// der sonst erst beim Nutzer auffällt: ein Key, den es in einer der beiden Sprachen
/// nicht gibt, oder ein Platzhalter, der zwischen EN und DE auseinanderläuft.
/// </summary>
[Collection("Localization")]
public class LocalizationTests : IDisposable
{
    private readonly CultureInfo _original = LocalizationService.Instance.Current;

    public void Dispose() => LocalizationService.Instance.Current = _original;

    [Fact]
    public void Englisch_und_Deutsch_werden_angeboten()
    {
        LocalizationService.SupportedCultures.Select(c => c.Iso).Should().Contain(["en", "de"]);
    }

    [Fact]
    public void Deutsche_Texte_kommen_auf_Deutsch()
    {
        LocalizationService.Instance.SetCulture("de");

        LocalizationService.Instance["Order_Buy"].Should().Be("Kaufen");
        LocalizationService.Instance["Portfolio_Section"].Should().Be("DEPOT");
    }

    [Fact]
    public void Englische_Texte_kommen_auf_Englisch()
    {
        LocalizationService.Instance.SetCulture("en");

        LocalizationService.Instance["Order_Buy"].Should().Be("Buy");
        LocalizationService.Instance["Portfolio_Section"].Should().Be("PORTFOLIO");
    }

    [Fact]
    public void Unbekannter_Key_faellt_sichtbar_auf()
    {
        LocalizationService.Instance["Gibt_Es_Nicht"].Should().Be("!Gibt_Es_Nicht!");
    }

    [Fact]
    public void Unbekannte_Sprache_faellt_auf_Englisch_zurueck()
    {
        LocalizationService.Instance.SetCulture("kli");

        LocalizationService.Instance["Order_Buy"].Should().Be("Buy");
    }

    [Fact]
    public void Jeder_Key_existiert_in_beiden_Sprachen()
    {
        var keys = ResxKeys("Strings.resx");
        var deutsch = ResxKeys("Strings.de.resx");

        keys.Should().NotBeEmpty();
        deutsch.Should().BeEquivalentTo(keys, "eine fehlende Übersetzung zeigt der App-Nutzer als !Key!");
    }

    [Fact]
    public void Platzhalter_stimmen_zwischen_den_Sprachen_ueberein()
    {
        // Ein {2} in der deutschen Fassung, das die englische nicht hat, wirft zur
        // Laufzeit eine FormatException — und zwar erst beim Nutzer.
        var en = ResxValues("Strings.resx");
        var de = ResxValues("Strings.de.resx");

        foreach (var (key, englisch) in en)
        {
            Placeholders(englisch).Should().BeEquivalentTo(
                Placeholders(de[key]),
                $"die Platzhalter von '{key}' müssen in beiden Sprachen gleich sein");
        }
    }

    private static IReadOnlyCollection<int> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{(\d+)\}")
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

    private static Dictionary<string, string> ResxValues(string fileName)
    {
        var path = FindResx(fileName);
        var doc = System.Xml.Linq.XDocument.Load(path);

        return doc.Root!.Elements("data")
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    private static IReadOnlyCollection<string> ResxKeys(string fileName) => ResxValues(fileName).Keys;

    private static string FindResx(string fileName)
    {
        // Vom Testausgabeverzeichnis nach oben zum Repo-Wurzelverzeichnis suchen.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Parkett", "Localization", fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{fileName} nicht gefunden.");
    }
}
