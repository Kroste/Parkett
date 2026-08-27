namespace Parkett.Persistence;

/// <summary>
/// Verschlüsselt einzelne Werte inline in der Klartext-JSON (Präfix <c>ENC1:</c>).
/// Bewusst NICHT die ganze Datei: Verhaltens-AV stuft entropiereiche Blobs im
/// Nutzerdatenordner als Ransomware ein. Inline sieht für die AV nach Konfiguration aus.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    /// <summary>Entschlüsselt. Liefert <c>null</c>, wenn der Wert nicht lesbar ist — nie eine Exception.</summary>
    string? Unprotect(string? storedValue);
}
