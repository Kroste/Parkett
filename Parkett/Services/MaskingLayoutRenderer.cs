using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.LayoutRenderers.Wrappers;

namespace Parkett.Services;

/// <summary>
/// Maskiert Geheimnisse in Log-Ausgaben (Kroste-Pflicht). MUSS vor dem ersten
/// Logger-Aufruf registriert werden, sonst schluckt NLog das Ende der Nachricht.
/// </summary>
[LayoutRenderer("masked")]
[ThreadAgnostic]
public sealed partial class MaskingLayoutRenderer : WrapperLayoutRendererBase
{
    private const string Replacement = "***";

    [GeneratedRegex(@"(?i)\b(api[_-]?key|token|password|passwort|secret|pwd)\s*[=:]\s*[^\s;]+")]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"(?i)(Password|Pwd)\s*=\s*[^;]+")]
    private static partial Regex ConnectionStringSecret();

    /// <summary>Lizenzschlüssel-Format payload.signatur — nie vollständig ins Log.</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{40,}\b")]
    private static partial Regex LicenseKeyLike();

    /// <summary>
    /// Registriert den Renderer, bevor irgendein Logger benutzt wird. Als ModuleInitializer,
    /// weil nlog.config ${masked:…} verwendet: ist der Renderer nicht registriert, verschluckt
    /// NLog das Ende jeder Nachricht und im Log steht nur noch "}". Das passiert sonst überall
    /// dort, wo Program.Main nicht der Einstiegspunkt ist — allen voran in den Tests.
    /// </summary>
    [ModuleInitializer]
    public static void Register()
    {
        LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<MaskingLayoutRenderer>("masked"));
    }

    public static string Mask(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var masked = KeyValueSecret().Replace(input, m => $"{m.Groups[1].Value}={Replacement}");
        masked = ConnectionStringSecret().Replace(masked, m => $"{m.Groups[1].Value}={Replacement}");
        masked = LicenseKeyLike().Replace(masked, Replacement);

        return masked;
    }

    protected override string Transform(string text) => Mask(text);
}
