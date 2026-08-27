# Parkett

## Grundlagen

- **Was:** Börsensimulator mit echten Marktdaten und virtuellem Geld. Zwei Vertriebswege:
  Steam (kostenlose Demo + Vollversion) und Direktverkauf (kostenlose Fassung + Pro mit
  eigenem Lizenzschlüssel).
- **Stack:** C# / .NET 10 / Avalonia 12.1.1, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection,
  NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management, `Directory.Build.props`,
  MinVer (Tags `v*`)
- **Konventionen:** GlobalExceptionHandler, AboutWindow mit Version + BMC-Button,
  `TreatWarningsAsErrors`, System-Tray, ChromeWindow für jedes Fenster
- **Kommunikation:** Deutsch, "du". Lars entwirft, Claude implementiert.

## Aktueller Stand

**v0.1.0 — Gerüst und Handelskern stehen, 76 Tests grün.**

Fertig:

- **Domänenkern** (`Domain/`): `Order`, `Fill`, `Position`, `Portfolio`, `MatchingEngine`,
  `TieredFeeModel`, `PerformanceCalculator`. Alles unveränderlich und ohne UI testbar.
- **Ausführungslogik**: Market-, Limit- und Stop-Orders. Käufe zum Brief, Verkäufe zum Geld.
  Deckungsprüfung inklusive Gebühr, Leerverkäufe gesperrt.
- **Handelssitzung** (`Services/TradingSession.cs`): Depot, Orderbuch, Ausführungen,
  Equity-Kurve, Kennzahlenbericht.
- **Marktdaten-Abstraktion** (`Services/`): `IMarketDataProvider` plus `MarketDataLicense`,
  die den Redistributionsstatus jeder Quelle *im Code* führt. `CsvHistoryProvider` liest
  mitgelieferte EOD-Historie.
- **Lizenzierung** (`Licensing/`): `Edition`/`Feature`/`FeatureGate` plus offline prüfbare
  Lizenzschlüssel (ECDSA P-256, kein Aktivierungsserver).
- **App-Gerüst**: DI, NLog mit Masking, GlobalExceptionHandler, Tray, ChromeWindow,
  AboutWindow, UpdateService mit echtem Self-Update, App-Icon, CI/Release-Workflows.
- **MainWindow**: Depot-Kennzahlen, Ordereingabe, Ausführungsliste, Statuszeile mit
  Datenquelle und Verzögerung.

Noch nicht gebaut: siehe Roadmap.

## Roadmap

Kurzfristig:

1. **Lokalisierung nachziehen (EN + DE).** Die Bausteine in `Localization/` sind da, die
   UI-Texte stehen aber noch fest im XAML. Kroste-Pflicht — vor dem ersten Release erledigen.
2. **Chart-Ansicht.** Candlestick-Darstellung der Historie mit Zeitachse, plus Marker für
   eigene Ausführungen. Ohne Chart fehlt dem Simulator die Hauptsache.
3. **Zeitsteuerung.** Sitzung Tag für Tag oder Kerze für Kerze vorwärts laufen lassen,
   mit Pause/Geschwindigkeit — das ist die eigentliche Spielschleife.
4. **Persistenz** nach `references/persistence.md`: Depot, Orderbuch und Verlauf über
   Neustarts halten (atomar via tmp+move, Lizenzschlüssel inline verschlüsselt).
5. **Datenbeschaffung**: Skript, das eine lizenzkonforme EOD-Historie in `Data/` erzeugt.

Mittelfristig:

6. **Alpaca-Provider (Bring your own key).** Nutzer hinterlegt seinen eigenen kostenlosen
   Zugang; damit verbreitet Parkett keine Daten weiter und braucht keine eigene Lizenz.
   Das ist der Weg zu „live" ohne fünfstellige Monatskosten.
7. **Deutsche-Börse-Provider.** Verzögerte Xetra-Daten sind kostenfrei, brauchen aber eine
   Data Usage Declaration. Erst freischalten, wenn `AgreementReference` gesetzt ist —
   `MarketDataLicense.IsUsableInPaidBuild` erzwingt das bereits.
8. **Steam-Integration.** Steamworks.NET oder Facepunch.Steamworks für Achievements und
   Entitlement. Demo als eigene App-ID. **Früh testen, ob das Steam-Overlay mit Avalonia
   funktioniert** — Avalonia rendert über Skia, das Overlay hängt an DirectX/OpenGL-Hooks.
9. **Strategie-Auswertung (Pro).** Bericht über mehrere Sitzungen, Export als CSV.

## Referenz

### Warum die Datenlizenz ein eigener Typ ist

`MarketDataLicense` steht bewusst im Code und nicht nur in der Doku. Die Rechtslage
entscheidet über die Wirtschaftlichkeit des Produkts:

| Datenart | Kosten / Auflage |
|---|---|
| Historisch, ≥ 1 voller Handelstag alt | keine Börsenlizenz nötig |
| 15 Min. verzögert (US) | ~250 $/Monat plus Verwaltungsgebühr |
| 15 Min. verzögert (Deutsche Börse) | kostenfrei, aber Data Usage Declaration nötig |
| Echtzeit konsolidiert | regelmäßig fünfstellig pro Monat |

Daraus folgt die Architektur: Die **Steam-Version** liefert Historie mit (auslieferbar),
die **Pro-Version** nutzt den *eigenen* Zugang des Nutzers (keine Weiterverbreitung).
Ein Provider mit `RequiresAgreement` bleibt gesperrt, solange kein Vertrag hinterlegt ist.

### Warum Gebühren ein eigenes Konzept sind

Der Lerneffekt eines Simulators liegt in den Transaktionskosten: 1 € pro Order sind bei
500 € Kapital und einem Roundtrip pro Woche über 20 % pro Jahr. Ein Simulator ohne
Gebühren erzieht zu genau dem Verhalten, das im Echtbetrieb Geld kostet. Deshalb steht
die Gebührensumme im Hauptfenster und `FeeDragPercent` ganz oben im Bericht.

### Ausführungsmodell

Käufe zum Brief, Verkäufe zum Geld — nie zur Mitte. Eine Ausführung zur Mitte schönt jede
Strategie um den halben Spread pro Trade. EOD-Quellen liefern nur einen Schlusskurs,
deshalb erzeugt `SyntheticSpread` daraus Geld/Brief.

### Lizenzschlüssel

ECDSA P-256 über `System.Security.Cryptography`, kein Fremdpaket. Format
`base64url(payload).base64url(signatur)`. Kein Aktivierungsserver — der wäre eine
Fehlerquelle und ein Datenschutzthema, ohne einen entschlossenen Cracker aufzuhalten.
Der **private Schlüssel gehört nie ins Repository**; der öffentliche steht als
`App.LicensePublicKey`. Auf Steam ist der Mechanismus ungenutzt: dort ist der App-Besitz
die Lizenz.

### Stolperfallen, die hier schon zugeschlagen haben

- **NLog-Masking muss registriert sein, bevor irgendein Logger läuft.** Sonst steht im Log
  nur `}` statt der Nachricht. Verlass dich nicht auf `Program.Main` — in Tests ist das
  nicht der Einstiegspunkt. Deshalb ist `MaskingLayoutRenderer.Register()` ein
  `[ModuleInitializer]`. `MaskingTests.Layout_rendert_die_vollstaendige_Nachricht`
  ist der Regressionstest dafür.
- **NLog 6.x**: `WrapperLayoutRendererBase` verlangt `Transform(string)`, nicht
  `TransformFormattedMesssage`. `[ThreadAgnostic]` liegt in `NLog.Config`.
- **xunit.v3 4.x + .NET-10-SDK**: Der VSTest-Pfad ist entfernt. Nötig sind `global.json`
  mit `"test": { "runner": "Microsoft.Testing.Platform" }`, `<OutputType>Exe</OutputType>`
  und `<UseMicrosoftTestingPlatformRunner>` im Testprojekt. `Microsoft.NET.Test.Sdk` und
  `xunit.runner.visualstudio` müssen **raus**, sonst zieht der alte Pfad wieder.
  Außerdem liefert xunit.v3 4.x kein implizites `using Xunit` mehr → `GlobalUsings.cs`.
- **App.axaml bringt DataGrid-Styles mit** — ohne `Avalonia.Controls.DataGrid` plus
  `StyleInclude` bricht der Build mit AVLN2000.
- **Avalonia 12**: `PlaceholderText` statt `Watermark`.

### Wichtige Klassen

| Klasse | Rolle |
|---|---|
| `Domain/MatchingEngine` | Order + Kurs → Ausführung, Ablehnung oder „bleibt offen" |
| `Domain/Portfolio` | Unveränderliches Depot, `Apply(Fill)` liefert den neuen Stand |
| `Domain/PerformanceCalculator` | Equity-Kurve + Ausführungen → Kennzahlen (FIFO-Rundläufe) |
| `Services/TradingSession` | Klammert alles zusammen, hält Orderbuch und Equity-Kurve |
| `Services/MarketDataLicense` | Rechtsrahmen einer Datenquelle, gatet den Verkaufsbuild |
| `Licensing/FeatureGate` | Einzige Tabelle, welche Funktion welche Stufe braucht |
