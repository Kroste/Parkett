using System.Text;
using Parkett.Persistence;

namespace Parkett.Tests;

/// <summary>
/// Deterministischer Ersatz für <see cref="SecretProtector"/>. Nötig, damit die Tests
/// plattformunabhängig laufen — DPAPI gibt es unter Linux nicht.
/// </summary>
public sealed class TestProtector : ISecretProtector
{
    public const string Prefix = "ENC1:";

    public string Protect(string plainText) =>
        Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));

    public string? Unprotect(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return null;
        }

        if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return storedValue;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(storedValue[Prefix.Length..]));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Temporäres Verzeichnis, das sich selbst aufräumt.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "parkett-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Aufräumen ist best effort — ein hängendes Temp-Verzeichnis darf keinen Test rot machen.
        }
    }
}
