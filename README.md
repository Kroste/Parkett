# Parkett

[![CI](https://github.com/Kroste/Parkett/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/Parkett/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/Parkett)](https://github.com/Kroste/Parkett/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Börsensimulator mit echten Kursen und virtuellem Geld — Desktop-App für Windows und Linux
(C# / .NET 10 / Avalonia 12).

Parkett handelt mit echten historischen Kursverläufen, aber ausschließlich mit Spielgeld.
Es ist ein Übungs- und Lernwerkzeug: Du siehst, was deine Entscheidungen im Depot bewirken —
einschließlich der Gebühren, die im Echtbetrieb den Unterschied machen.

> **Kein echtes Geld, keine Anlageberatung.** Parkett gibt keine Kauf- oder
> Verkaufsempfehlungen und ist weder Broker noch Finanzdienstleister.

<!-- Screenshot: docs/screenshot.png einfügen, sobald die UI steht -->

## Features

- **Echte Kursverläufe:** Handel gegen historische Tagesdaten statt gegen einen Zufallsgenerator.
- **Kerzenchart mit Zeitsteuerung:** Die Sitzung läuft Kerze für Kerze vorwärts — Start,
  Pause, Einzelschritt und vier Tempostufen. Der Chart zeigt nie eine Kerze, die du zum
  Entscheidungszeitpunkt nicht kennen konntest.
- **Realistische Ausführung:** Käufe zum Briefkurs, Verkäufe zum Geldkurs — nie zur Mitte.
  Market-, Limit- und Stop-Orders, Deckungsprüfung inklusive Gebühr.
- **Gebühren, die wehtun:** Wählbares Gebührenmodell (Neobroker, Hausbank, gebührenfrei).
  Die Gebührensumme steht dauerhaft im Hauptfenster.
- **Kennzahlen:** Gesamtwert, realisiertes Ergebnis, maximaler Rückgang, Trefferquote
  nach Gebühren.
- **Sitzung fortsetzen:** Beim Beenden wird der Stand gesichert und beim nächsten Start
  an derselben Kerze fortgesetzt.
- **Zweisprachig:** Deutsch und Englisch, live umschaltbar ohne Neustart.
- **Transparente Datenquelle:** Die Statuszeile nennt immer Quelle und Verzögerung.
- 🔄 **Selbstaktualisierung:** Parkett prüft beim Start (und jederzeit über *Über Parkett*),
  ob eine neuere Fassung vorliegt, lädt sie auf Wunsch herunter, tauscht sich aus und
  startet neu. Ohne Zustimmung passiert nichts.

## Installation

Fertige Pakete gibt es auf der [Releases-Seite](https://github.com/Kroste/Parkett/releases):

**Windows:** `Parkett-X.Y.Z-win-x64.zip` herunterladen, entpacken, `Parkett.exe` starten.
Keine Installation nötig (self-contained, .NET ist enthalten).

**Linux (AppImage, empfohlen):**

```bash
chmod +x Parkett-*-x86_64.AppImage
./Parkett-*-x86_64.AppImage
```

**Linux (tar.gz):** `Parkett-X.Y.Z-linux-x64.tar.gz` entpacken und `./Parkett` starten.

## Bedienung

1. **Instrument wählen und „Neue Sitzung"** — im Auslieferungszustand liegt `DEMO` bereit,
   ein erfundenes Wertpapier für den ersten Rundgang. Der Chart zeigt zunächst 60 Kerzen
   Vorlauf, damit du überhaupt etwas zu deuten hast.
2. **Vorwärts laufen lassen** — „Start" lässt die Zeit weiterlaufen, „Schritt" deckt genau
   eine Kerze auf. Das Tempo stellst du daneben ein.
3. **Kaufen oder Verkaufen** — Stückzahl setzen und klicken. Lässt du Limit und Stop leer,
   wird billigst bzw. bestens ausgeführt; mit Limit oder Stop wandert die Order ins Buch
   und wartet auf den Kurs. Reicht das Guthaben nicht, sagt die Statuszeile warum.
4. **Ausführungen prüfen** — jede Ausführung landet mit Kurs und Gebühr in der Liste und
   als Dreieck im Chart.
5. **Sitzungsende** — ist die Historie durchgelaufen, fasst die Statuszeile Ergebnis,
   Rundläufe, Trefferquote und Gebührenlast zusammen.

Das Depot startet mit 10.000 € Spielgeld. Leerverkäufe sind bewusst gesperrt.

Beim Beenden wird die laufende Sitzung gesichert — beim nächsten Start bringt dich
„Fortsetzen" an dieselbe Kerze zurück.

## Kursdaten

Parkett liest Kurse aus `Data/` (eine CSV je Symbol, `Date,Open,High,Low,Close,Volume`).
Mitgeliefert wird nur `DEMO.csv` mit erfundenen Kursen.

Welche echten Daten du dort ablegen darfst, hängt von ihrer Herkunft ab — historische
Schlusskurse ab einem vollen Handelstag Alter sind unkritisch, verzögerte und
Echtzeitkurse brauchen eine Vereinbarung mit der jeweiligen Börse. Details in
[`Parkett/Data/README.md`](Parkett/Data/README.md).

## Einstellungen

Über das Zahnrad oben rechts:

- **Sprache** — Deutsch oder Englisch, wirkt sofort in allen Fenstern.
- **Gebührenmodell** — ohne Gebühren, Neobroker (1,00 € je Order) oder Hausbank
  (4,90 € + 0,25 %, mindestens 9,90 €). Dieselbe Strategie mit verschiedenen Modellen zu
  spielen ist der eigentliche Lerneffekt.
- **Lizenzschlüssel** — für die Pro-Version aus dem Direktverkauf.

Einstellungen und Sitzungsstand liegen unter `%APPDATA%\Parkett` bzw. `~/.config/Parkett`.
Der Lizenzschlüssel der Pro-Version liegt unter:

- Windows: `%APPDATA%\Parkett\settings.json` (Schlüssel verschlüsselt via DPAPI)
- Linux: `~/.config/Parkett/settings.json` (Schlüssel verschlüsselt via AES-256-GCM)

## Logs & Fehlersuche

Logdateien liegen im Nutzerprofil (Tagesarchiv, 14 Tage):

- Windows: `%APPDATA%\Parkett\logs\`
- Linux: `~/.config/Parkett/logs/`

Bei einem Problem bitte ein Issue mit der aktuellen Logdatei eröffnen — Passwörter,
Tokens und Lizenzschlüssel werden automatisch maskiert.

## Entwicklung

Das App-Icon wird aus einem Skript erzeugt, damit es reproduzierbar bleibt —
`scripts/build_icon.py` (Pillow) und `scripts/build_icon.ps1` (System.Drawing,
ohne Python-Abhängigkeit) liefern dasselbe Ergebnis. Bei Design-Änderungen
beide anpassen.

```bash
dotnet build   # bauen
dotnet test    # Tests (134 Stück)
dotnet run --project Parkett
```

Release: VS-Code-Task „release (tag + push)" — prüft den Git-Zustand, setzt den Tag und
stößt die GitHub-Action an, die alle Pakete baut.

## Lizenz

MIT — siehe [LICENSE](LICENSE).
