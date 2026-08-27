using System.Globalization;

namespace Parkett.Localization;

/// <summary>Kurzform für lokalisierte Texte im Code — spart das ausgeschriebene Singleton.</summary>
internal static class L
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance[key], args);
}
