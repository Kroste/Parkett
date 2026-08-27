# Kursdaten

Dieser Ordner wird zur Laufzeit nach `<Anwendungsverzeichnis>/Data` kopiert und vom
`CsvHistoryProvider` gelesen. Eine Datei je Symbol, Dateiname = Symbol.

Format (Kopfzeile optional):

```
Date,Open,High,Low,Close,Volume
2026-03-02,100.50,102.00,99.50,101.25,1234567
```

## Was hier liegen darf — und was nicht

Der Ordner entscheidet über die Rechtslage des ganzen Produkts:

| Datenart | Auslieferbar? |
|---|---|
| Historische Schlusskurse, mindestens einen vollen Handelstag alt | **ja**, ohne Börsenlizenz |
| 15 Minuten verzögerte Intraday-Kurse | nur mit Vereinbarung des Datenanbieters bzw. der Börse |
| Echtzeitkurse | nur mit Vertriebslizenz der jeweiligen Börse (fünfstellig pro Monat) |

**Vor dem ersten Verkauf zu klären:** Der Lizenzvertrag des Datenanbieters (z. B. EODHD)
regelt zusätzlich, ob die Daten *weiterverbreitet* werden dürfen. „Historische Kurse sind
lizenzfrei anzeigbar" heißt nicht automatisch, dass der Vertrag des Anbieters das Bündeln
im Produkt erlaubt — beides muss stimmen.

Für Kurse der Deutschen Börse (Xetra, DAX) gibt es verzögerte Daten kostenfrei, aber nur
gegen unterschriebene Data Usage Declaration. Kontakt: data.services@deutsche-boerse.com.

## DEMO.csv

`DEMO.csv` enthält **erfundene** Kurse eines Fantasie-Wertpapiers, damit die Anwendung
ohne Datenlizenz startklar ist. Kein realer Kursverlauf, keine reale Gesellschaft.
