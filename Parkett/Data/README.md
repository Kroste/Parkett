# Kursdaten

Dieser Ordner wird zur Laufzeit nach `<Anwendungsverzeichnis>/Data` kopiert und vom
`CsvHistoryProvider` gelesen. Eine Datei je Symbol, Dateiname = Symbol.

Format (Kopfzeile optional):

```
Date,Open,High,Low,Close,Volume
2026-03-02,100.50,102.00,99.50,101.25,1234567
```

## Eigene Kurse hereinholen

`scripts/fetch_history.py` schreibt genau dieses Format. Zwei Wege:

**Aus einer Datei, die du schon hast** — Broker-Export, Tabellenkalkulation, ein
manueller Download bei einem Anbieter deiner Wahl:

```bash
python scripts/fetch_history.py csv ~/Downloads/sap.csv --symbol SAP
```

Deutsche Exporte werden erkannt: Semikolon als Trennzeichen, Komma als
Dezimaltrenner, `DD.MM.YYYY`, Spaltennamen wie `Datum` / `Schlusskurs` / `Umsatz`.
Fehlen Eröffnung, Hoch und Tief, wird der Schlusskurs eingesetzt — für einen
EOD-Simulator ehrlicher als eine erfundene Tagesspanne.

**Über eine API mit deinem eigenen Zugang:**

```bash
export ALPHAVANTAGE_KEY=dein_schluessel      # Windows: $env:ALPHAVANTAGE_KEY = "..."
python scripts/fetch_history.py alphavantage AAPL MSFT
```

Der kostenlose Schlüssel (25 Abrufe/Tag) reicht für ein Dutzend Instrumente.

Das Skript verwirft dabei automatisch alle Kerzen, die jünger als ein voller
Handelstag sind — siehe unten, warum das die entscheidende Grenze ist.

## Was hier liegen darf — und was nicht

Der Ordner entscheidet über die Rechtslage des ganzen Produkts. **Zwei Fragen
müssen getrennt beantwortet werden**, und beide müssen passen:

**1. Braucht die Datenart eine Börsenlizenz?**

| Datenart | Auslieferbar? |
|---|---|
| Historische Schlusskurse, mindestens einen vollen Handelstag alt | **ja**, ohne Börsenlizenz |
| 15 Minuten verzögerte Intraday-Kurse | nur mit Vereinbarung des Datenanbieters bzw. der Börse |
| Echtzeitkurse | nur mit Vertriebslizenz der jeweiligen Börse (fünfstellig pro Monat) |

**2. Erlaubt der Vertrag des Anbieters die Weiterverbreitung?**

Praktisch nie — jedenfalls nicht bei kostenlosen Zugängen. „Historische Kurse sind
lizenzfrei anzeigbar" heißt eben *nicht*, dass der Vertrag des Anbieters das Bündeln
im Produkt erlaubt.

**Daraus folgt die Arbeitsteilung:** Parkett liefert das Skript, du holst die Daten.
Der Schlüssel gehört dir, der Vertrag auch, und Parkett verbreitet nichts weiter.
Dasselbe Muster ist für den geplanten Alpaca-Provider vorgesehen.

Deshalb sind selbst geholte `*.csv` in diesem Ordner **von `.gitignore` ausgenommen** —
ein Repository ist eine Weiterverbreitung. Nur `DEMO.csv` ist eingecheckt.

Für Kurse der Deutschen Börse (Xetra, DAX) gibt es verzögerte Daten kostenfrei, aber nur
gegen unterschriebene Data Usage Declaration. Kontakt: data.services@deutsche-boerse.com.

### Warum Stooq nicht eingebaut ist

Stooq wäre die bequemste Quelle — direkte CSV-Downloads ohne Registrierung. Es sind
dort aber keine Nutzungsbedingungen auffindbar, und der CAPTCHA vor dem Bulk-Download
deutet darauf hin, dass automatisiertes Abrufen nicht vorgesehen ist. Dazu kommt das
europäische Datenbankrecht, das eine Sammlung auch dann schützt, wenn der einzelne
Kurs nicht schutzfähig ist.

Ein Skript im Produkt, das dort automatisch abgreift, würde diese Entscheidung für
alle Nutzer treffen. Wer von Hand herunterlädt, entscheidet für sich selbst — und
bringt die Datei über den `csv`-Modus herein.

## DEMO.csv

`DEMO.csv` enthält **erfundene** Kurse eines Fantasie-Wertpapiers, damit die Anwendung
ohne Datenlizenz startklar ist. Kein realer Kursverlauf, keine reale Gesellschaft.
