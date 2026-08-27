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

**v0.2.0 — spielbar. 134 Tests grün, unter Xvfb end-to-end verifiziert
(Sitzung starten, kaufen, Schritte, verkaufen, Sprachwechsel).**

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
  AboutWindow, App-Icon, CI/Release-Workflows.
- **Self-Update**: `UpdateService` lädt das Release-Asset und startet das
  Austausch-Skript. Zwei Einstiege — der Knopf im Über-Fenster und ein nicht
  blockierender Check beim Start, der bei einem Fund `UpdatePromptWindow` zeigt.
  Beide beenden die App nach dem Start des Installers.
- **Zeitsteuerung** (`Simulation/`): `SimulationClock` deckt die Historie Kerze für Kerze
  auf, mit Vorlauf, Play/Pause/Schritt und vier Tempostufen.
- **Chart** (`Charting/` + `Controls/CandlestickChart.cs`): Kerzen, Preisraster auf runden
  Werten, kulturgerechte Zeitachse, Fadenkreuz, Marker für eigene Ausführungen.
- **Persistenz** (`Persistence/`): Einstellungen und unterbrochene Sitzung, atomar
  geschrieben, Lizenzschlüssel inline verschlüsselt.
- **Lokalisierung**: EN + DE, Wechsel live in allen Fenstern.
- **Einstellungen-Fenster**: Sprache, Gebührenmodell, Lizenzschlüssel.
- **MainWindow**: Chart, Transportleiste, Depot-Kennzahlen, Order mit Limit und Stop,
  Buch der offenen Orders, Ausführungsliste, Statuszeile mit Datenquelle und Verzögerung.

Noch nicht gebaut: siehe Roadmap.

## Roadmap

Kurzfristig:

1. **Datenbeschaffung.** Skript, das eine lizenzkonforme EOD-Historie nach `Data/` holt
   (siehe `Parkett/Data/README.md`). Ohne echte Instrumente bleibt es beim DEMO-Wert.
2. **Abschlussbericht als Fenster.** Die Sitzung endet aktuell mit einer Statuszeile.
   Ein eigener Bericht mit Equity-Kurve, Drawdown und Gebührenlast ist der Moment, in
   dem der Simulator seine Lehre erteilt.
3. **Mehrere Instrumente pro Sitzung.** Depot über mehrere Werte, Umschalten des Charts.
   `Portfolio` kann das bereits, `SimulationClock` läuft noch auf einem Symbol.
4. **Achievements-fähige Meilensteine** vorbereiten (erste Sitzung beendet, ohne
   Totalverlust überstanden, 20 Rundläufe) — Aufhänger für die Steam-Integration.

Mittelfristig:

5. **Alpaca-Provider (Bring your own key).** Nutzer hinterlegt seinen eigenen kostenlosen
   Zugang; damit verbreitet Parkett keine Daten weiter und braucht keine eigene Lizenz.
   Das ist der Weg zu „live" ohne fünfstellige Monatskosten.
6. **Deutsche-Börse-Provider.** Verzögerte Xetra-Daten sind kostenfrei, brauchen aber eine
   Data Usage Declaration. Erst freischalten, wenn `AgreementReference` gesetzt ist —
   `MarketDataLicense.IsUsableInPaidBuild` erzwingt das bereits.
7. **Steam-Integration.** Steamworks.NET oder Facepunch.Steamworks für Achievements und
   Entitlement. Demo als eigene App-ID. **Früh testen, ob das Steam-Overlay mit Avalonia
   funktioniert** — Avalonia rendert über Skia, das Overlay hängt an DirectX/OpenGL-Hooks.
8. **Strategie-Auswertung (Pro).** Bericht über mehrere Sitzungen, Export als CSV.

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
- **Ein Provider, der seine `MarketDataLicense` in einem Feld hält, friert die Sprache ein,
  die beim App-Start galt.** Als Property halten, dann greift der Live-Wechsel.
- **`AffectsRender<T>` nicht vergessen**, wenn ein selbstgezeichnetes Control neue
  StyledProperties bekommt — sonst bleibt der Chart nach einem Datenwechsel stehen.
- **Quarantäne gehört an `JsonException`, nicht an `Exception`.** `JsonStore.Load`
  hat anfangs auch bei `IOException` nach `.broken` verschoben — bei einer nur
  kurz gesperrten Datei (Virenscanner, Netzlaufwerk) räumt das intakte Einstellungen
  und die laufende Sitzung weg. Genau der Verlust, den die Rettung verhindern soll.
- **Der Regressionstest dazu braucht `FileShare.Delete`, nicht `FileShare.None`.**
  Mit `None` scheitert auch der `.broken`-Move, und der Test ist unter beiden
  Fassungen grün — er beweist nichts. `Delete` blockiert das Lesen und erlaubt das
  Umbenennen; erst damit wird die alte Fassung rot. Der Test läuft nur unter Windows
  (`Assert.SkipUnless`), weil Linux keine verbindlichen Dateisperren kennt.
- **Ein Self-Update, das die App nicht selbst beendet, hängt bei 100 %.** Das
  Austausch-Skript wartet auf das Prozessende (`Wait-Process` bzw. `kill -0`).
  `DownloadAndApplyAsync` gibt nur `true` zurück — **jeder** Aufrufer muss danach
  `UpdateService.TerminateForUpdate()` rufen. Hier waren Download und Beenden zwei
  Releases lang fertig implementiert, aber von keiner Stelle aufgerufen: das
  Über-Fenster prüfte nur und zeigte „Version X verfügbar". Wer den Update-Pfad
  anfasst, prüft mit `grep -rn DownloadAndApplyAsync`, dass es noch einen Aufrufer gibt.

### Sprachwechsel: was live geht und was nicht

`{loc:Tr Key}` im XAML aktualisiert sich von selbst — das erledigt der statisch gecachte
`LocalizedString`-Wrapper. **Alles, was ein ViewModel als fertigen String liefert, nicht.**
Beide ViewModels hängen sich deshalb an `LocalizationService.PropertyChanged` und rendern
ihre abgeleiteten Texte neu. Zwei Details, die dabei zählen:

- Statusmeldungen werden als `(Key, Args)` gemerkt, nicht als fertiger Text — sonst lässt
  sich eine bereits gesetzte Meldung gar nicht mehr übersetzen.
- Eine ComboBox rendert ihre Einträge einmal über `ToString` und baut nur bei einem echten
  `ItemsSource`-Wechsel neu auf. Dieselbe Listeninstanz erneut zu melden reicht nicht.

**Bewusst nicht mitgeschaltet:** `CultureInfo.CurrentCulture`. Zahlen und Datumsangaben
folgen weiter der OS-Kultur. Sie mitzuschalten würde mitten in der Sitzung das
Dezimaltrennzeichen der Eingabefelder ändern — ein Nutzer, der gerade „103,50" getippt hat,
hätte plötzlich eine ungültige Eingabe.

### Bekannte Einschränkung: Flaggen-Emoji

Der Sprachumschalter nutzt Regional-Indicator-Emoji (🇩🇪/🇬🇧). Windows rendert die
grundsätzlich nicht als Flaggen, sondern als Buchstabenpaar; im Testcontainer ohne
Emoji-Font ebenfalls. Unter Linux mit Noto Color Emoji sieht es richtig aus. Wenn es
überall gleich aussehen soll, müssen kleine Flaggen-PNGs als Assets mitgeliefert werden.

### Wichtige Klassen

| Klasse | Rolle |
|---|---|
| `Domain/MatchingEngine` | Order + Kurs → Ausführung, Ablehnung oder „bleibt offen" |
| `Domain/Portfolio` | Unveränderliches Depot, `Apply(Fill)` liefert den neuen Stand |
| `Domain/PerformanceCalculator` | Equity-Kurve + Ausführungen → Kennzahlen (FIFO-Rundläufe) |
| `Services/TradingSession` | Klammert alles zusammen, hält Orderbuch und Equity-Kurve |
| `Services/MarketDataLicense` | Rechtsrahmen einer Datenquelle, gatet den Verkaufsbuild |
| `Licensing/FeatureGate` | Einzige Tabelle, welche Funktion welche Stufe braucht |
| `Simulation/SimulationClock` | Deckt die Historie Kerze für Kerze auf, ohne eigenen Timer |
| `Charting/ChartViewport` | Komplette Chart-Skalierung, ohne Avalonia-Bezug und testbar |
| `Controls/CandlestickChart` | Zeichnet nur; Farben kommen als StyledProperty von außen |
| `Persistence/JsonStore` | Atomares Speichern, `.broken`-Rettung **nur bei kaputtem JSON**, stürzt nie an Nutzerdaten |
| `Services/UpdateService` | Release-Check, Download, plattformeigenes Austausch-Skript |
