using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using NLog;

namespace Parkett.Services;

public sealed record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion, string? AssetUrl, string? ReleaseUrl);

/// <summary>
/// Update-Check UND echtes Self-Update gegen GitHub Releases (Kroste-Standard).
/// Proxy-aware, nicht blockierend, Installation nur nach Zustimmung.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string Owner = "Kroste";
    private const string Repo = "Parkett";

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private UpdateCheckResult? _cached;

    public UpdateService()
    {
        // Firmen-Proxy (Kerberos/Negotiate) auf dem Arbeitslaptop; unter Linux ein No-Op.
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repo}/{AppVersion}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Instanz-Zugriff auf <see cref="AppVersion"/> — das AboutWindow bindet daran.</summary>
    public string CurrentVersion => AppVersion;

    /// <summary>Version aus der Assembly (MinVer schreibt sie als InformationalVersion).</summary>
    public static string AppVersion
    {
        get
        {
            var raw = Assembly.GetExecutingAssembly()
                          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "0.0.0";

            // Build-Metadaten (+sha) abschneiden.
            var plus = raw.IndexOf('+');
            return plus > 0 ? raw[..plus] : raw;
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var stopwatch = Stopwatch.StartNew();
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

        try
        {
            Log.Info("Update-Check startet: {Url}", url);

            var release = await _http.GetFromJsonAsync<GitHubRelease>(url, cancellationToken).ConfigureAwait(false);

            if (release?.TagName is null)
            {
                Log.Warn("Update-Check ohne verwertbare Antwort nach {Ms} ms.", stopwatch.ElapsedMilliseconds);
                return _cached = new UpdateCheckResult(false, null, null, null);
            }

            var latest = release.TagName.TrimStart('v', 'V');
            var isNewer = IsNewer(latest, AppVersion);
            var asset = SelectAsset(release, latest);

            Log.Info(
                "Update-Check fertig nach {Ms} ms: aktuell {Current}, verfügbar {Latest}, neuer: {IsNewer}, Asset: {Asset}",
                stopwatch.ElapsedMilliseconds, AppVersion, latest, isNewer, asset ?? "keins");

            return _cached = new UpdateCheckResult(isNewer, latest, asset, release.HtmlUrl);
        }
        catch (Exception ex)
        {
            // Offline oder Proxy-Problem darf die App nie stören — nur loggen.
            Log.Warn(ex, "Update-Check fehlgeschlagen nach {Ms} ms.", stopwatch.ElapsedMilliseconds);
            return new UpdateCheckResult(false, null, null, null);
        }
    }

    /// <summary>
    /// Lädt das Asset und startet das plattformspezifische Austausch-Skript.
    /// Bei <c>true</c> MUSS der Aufrufer <see cref="TerminateForUpdate"/> rufen —
    /// das Skript wartet auf das Prozessende, sonst hängt das Update bei 100 %.
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(
        string assetUrl,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetUrl);

        try
        {
            var workDir = Path.Combine(Path.GetTempPath(), $"{Repo}-update-{Environment.ProcessId}");
            Directory.CreateDirectory(workDir);

            var fileName = Path.GetFileName(new Uri(assetUrl).LocalPath);
            var downloadPath = Path.Combine(workDir, fileName);

            await DownloadAsync(assetUrl, downloadPath, progress, cancellationToken).ConfigureAwait(false);
            Log.Info("Update-Asset geladen: {Path}", downloadPath);

            var script = OperatingSystem.IsWindows()
                ? WriteWindowsInstaller(workDir, downloadPath)
                : WriteLinuxInstaller(workDir, downloadPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows() ? $"/c \"{script}\"" : $"\"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            });

            Log.Info("Installer gestartet: {Script}", script);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Self-Update fehlgeschlagen.");
            return false;
        }
    }

    /// <summary>
    /// Beendet die App, damit der Installer weiterlaufen kann. Von JEDEM Aufrufer
    /// von <see cref="DownloadAndApplyAsync"/> bei Erfolg zu rufen.
    /// </summary>
    public static void TerminateForUpdate()
    {
        Log.Info("App beendet sich für den Update-Austausch.");
        LogManager.Flush();

        // Fail-Safe: falls Exit an einem Finalizer hängen bleibt, hart nachlegen.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500).ConfigureAwait(false);
            Process.GetCurrentProcess().Kill();
        });

        Environment.Exit(0);
    }

    private async Task DownloadAsync(string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0L;
        var buffer = new byte[81920];
        var read = 0L;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(targetPath);

        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            read += count;

            if (total > 0)
            {
                progress?.Report((double)read / total);
            }
        }
    }

    /// <summary>
    /// Batch-Zeilen OHNE führende Einrückung schreiben — ein eingerücktes :label ist für cmd
    /// kein gültiges Sprungziel, das goto scheitert still und die ALTE Version startet neu.
    /// </summary>
    private static string WriteWindowsInstaller(string workDir, string zipPath)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exePath = Path.Combine(appDir, $"{Repo}.exe");
        var extractDir = Path.Combine(workDir, "new");
        var scriptPath = Path.Combine(workDir, "install.bat");

        var lines = new[]
        {
            "@echo off",
            $"set LOG=\"{Path.Combine(workDir, "update.log")}\"",
            $"echo Warte auf Prozessende {Environment.ProcessId} >> %LOG%",
            $"powershell -NoProfile -Command \"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue\"",
            "timeout /t 2 /nobreak > nul",
            $"powershell -NoProfile -Command \"Expand-Archive -LiteralPath '{zipPath}' -DestinationPath '{extractDir}' -Force\" >> %LOG% 2>&1",
            $"xcopy /E /Y /I \"{extractDir}\\*\" \"{appDir}\\\" >> %LOG% 2>&1",
            $"start \"\" \"{exePath}\"",
            "exit",
        };

        File.WriteAllLines(scriptPath, lines);
        return scriptPath;
    }

    /// <summary>
    /// Log NICHT nach BaseDirectory/logs — beim laufenden AppImage ist das ein read-only
    /// Squashfs-Mount, bash bricht sofort ab und die App wird nie ersetzt.
    /// </summary>
    private static string WriteLinuxInstaller(string workDir, string assetPath)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        var scriptPath = Path.Combine(workDir, "install.sh");

        var stateDir = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        var logPath = Path.Combine(stateDir, Repo, "update.log");

        var body = new List<string>
        {
            "#!/bin/bash",
            $"mkdir -p \"$(dirname '{logPath}')\" 2>/dev/null || true",
            $"exec >>'{logPath}' 2>&1 || exec >>/tmp/{Repo}-update.log 2>&1",
            "set -x",
            $"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.5; done",
            "sleep 1",
        };

        if (!string.IsNullOrWhiteSpace(appImage))
        {
            // "Text file busy": das laufende AppImage ist als Loop-Device gemountet —
            // cp -f behält den Inode, mv/rm scheitern.
            body.Add($"cp -f '{assetPath}' '{appImage}'");
            body.Add($"chmod +x '{appImage}'");
            body.Add($"setsid '{appImage}' >/dev/null 2>&1 &");
        }
        else
        {
            body.Add($"tar -xzf '{assetPath}' -C '{appDir}'");
            body.Add($"chmod +x '{appDir}/{Repo}'");
            body.Add($"setsid '{appDir}/{Repo}' >/dev/null 2>&1 &");
        }

        body.Add("exit 0");

        // Hart '\n' statt AppendLine: unter Windows erzeugte CRLF-Zeilen brechen bash.
        File.WriteAllText(scriptPath, string.Join('\n', body) + "\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return scriptPath;
    }

    /// <summary>Wählt das Asset für die laufende Plattform. Namensschema kommt aus release.yml.</summary>
    private static string? SelectAsset(GitHubRelease release, string version)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

        var candidates = OperatingSystem.IsWindows()
            ? new[] { $"{Repo}-{version}-win-{arch}.zip" }
            : new[] { $"{Repo}-{version}-x86_64.AppImage", $"{Repo}-{version}-linux-{arch}.tar.gz" };

        foreach (var candidate in candidates)
        {
            var match = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, candidate, StringComparison.OrdinalIgnoreCase));

            if (match?.BrowserDownloadUrl is not null)
            {
                return match.BrowserDownloadUrl;
            }
        }

        return null;
    }

    /// <summary>Semantischer Vergleich — Stringvergleich stuft 1.10.0 fälschlich unter 1.9.0 ein.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        static Version Parse(string value)
        {
            var core = value.Split('-', '+')[0];
            return Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0, 0);
        }

        return Parse(candidate) > Parse(current);
    }

    public void Dispose() => _http.Dispose();

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}
