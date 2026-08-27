# Parkett

## Grundlagen

- **Was:** Börsensimulator mit echten Marktdaten und virtuellem Geld. Zwei Vertriebswege
  vorgesehen: Direktverkauf (kostenlose Fassung + Pro mit eigenem Lizenzschlüssel) und
  später Steam (kostenlose Demo + Vollversion). **Der Direktverkauf hat Vorrang** —
  Steam ist zurückgestellt, siehe Roadmap.
- **Stack:** C# / .NET 10 / Avalonia 12.1.1, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection,
  NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management, `Directory.Build.props`,
  MinVer (Tags `v*`)
- **Konventionen:** GlobalExceptionHandler, AboutWindow mit Version + BMC-Button,
  `TreatWarningsAsErrors` **plus `EnforceCodeStyleInBuild`** (Stilverstöße sind
  Compile-Fehler), System-Tray, ChromeWindow für jedes Fenster, echte Umlaute in
  jedem deutschen Text
- **Kommunikation:** Deutsch, "du". Lars entwirft, Claude implementiert.

## Aktueller Stand

**v0.2.0 — spielbar. 163 Tests grün (plus 23 Skript-Tests), unter Xvfb end-to-end verifiziert
(Sitzung starten, kaufen, Schritte, verkaufen, Sprachwechsel).**

Am 2026-08-27 gegen den `kroste-avalonia`-Skill geprüft und nachgezogen: Self-Update
angeschlossen, JsonStore-Quarantäne eingegrenzt, Emoji-Font-Fallback, Log-Pfade,
Umlaute, `.editorconfig`, CI-Annotationen, VM-Aufteilung. Details in der Referenz.

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
- **Abschlussbericht** (`Views/ReportWindow`): öffnet sich, wenn die Historie
  durchgelaufen ist. Gebührenlast ganz oben, Equity-Kurve mit Startkapital-Linie und
  Drawdown-Marker, Kennzahlen, und ein Urteil in Worten. Vorschau ohne Sitzung über
  `Parkett.exe --report-preview <datei.png> [gewinn|verlust|gebuehren|leer]`.
- **Datenbeschaffung** (`scripts/fetch_history.py`): holt EOD-Historie nach `Data/` —
  aus einer vorhandenen Datei (auch deutsche Broker-Exporte) oder über Alpha Vantage
  mit dem Schlüssel des Nutzers. Erzwingt die Handelstag-Grenze im Code. Eigene
  Selbsttests (`scripts/test_fetch_history.py`), die in der CI mitlaufen.

Noch nicht gebaut: siehe Roadmap.

## Roadmap

**Steam ist zurückgestellt** (Stand 2026-08-27). Der Fokus liegt auf dem
Direktverkauf: eine Fassung, die für sich steht und keine Plattform braucht.
Alles, was allein wegen Steam auf der Liste stand, ist entsprechend nach hinten
gewandert — der Punkt selbst bleibt mit allen offenen Fragen erhalten, damit er
beim Wiederaufgreifen vollständig ist.

Kurzfristig:

1. **Mehrere Instrumente pro Sitzung.** Depot über mehrere Werte, Umschalten des Charts.
   `Portfolio` kann das bereits, `SimulationClock` läuft noch auf einem Symbol.
   **Hängt daran:** die Equity-Kurve über mehrere Werte, und ob der Bericht dann
   pro Instrument oder für das Gesamtdepot aufschlüsselt.
2. **Bericht exportieren.** Der Bericht rendert sich bereits selbst in eine PNG
   (`--report-preview`); dieselbe Mechanik als „Bericht speichern"-Knopf im Fenster
   wäre wenig Aufwand und macht Sitzungen vergleichbar.

Mittelfristig:

3. **Alpaca-Provider (Bring your own key).** Nutzer hinterlegt seinen eigenen kostenlosen
   Zugang; damit verbreitet Parkett keine Daten weiter und braucht keine eigene Lizenz.
   Das ist der Weg zu „live" ohne fünfstellige Monatskosten.
4. **Deutsche-Börse-Provider.** Verzögerte Xetra-Daten sind kostenfrei, brauchen aber eine
   Data Usage Declaration. Erst freischalten, wenn `AgreementReference` gesetzt ist —
   `MarketDataLicense.IsUsableInPaidBuild` erzwingt das bereits.
5. **Strategie-Auswertung (Pro).** Bericht über mehrere Sitzungen, Export als CSV.
6. **Meilensteine** (erste Sitzung beendet, ohne Totalverlust überstanden, 20 Rundläufe).
   Der Abschlussbericht ist der natürliche Ort, sie auszulösen. Sie waren ursprünglich
   nur als Steam-Vorbereitung gedacht, haben aber auch im Direktverkauf Wert — deshalb
   bleiben sie auf der Liste, nur ohne Eile.

Später — Steam:

7. **Steam-Integration.** Steamworks.NET oder Facepunch.Steamworks für Achievements und
   Entitlement. Demo als eigene App-ID. Drei Dinge sind vorher zu klären:
   - **Overlay-Risiko zuerst prüfen.** Avalonia rendert über Skia, das Steam-Overlay
     hängt an DirectX/OpenGL-Hooks. Funktioniert das nicht, ändert es die Machbarkeit
     des ganzen Wegs — deshalb ein Wegwerf-Prototyp *vor* jeder Integrationsarbeit.
   - **Welche Kursdaten liegen bei?** Auf Steam muss ausgeliefert werden, und das
     braucht einen Anbieter, der die Weiterverbreitung schriftlich erlaubt (siehe
     Referenz). Die lizenzfreie Alternative sind erfundene Verläufe wie `DEMO.csv` —
     ein Generator für Szenarien (Crash, Seitwärtsmarkt, Blase) wäre überschaubar
     und didaktisch sogar im Vorteil, weil sich Lehrfälle gezielt bauen lassen.
   - **Der Lizenzschlüssel-Mechanismus bleibt dort ungenutzt** — auf Steam ist der
     App-Besitz die Lizenz. `FeatureGate` deckt beides bereits ab.

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

**Die Tabelle beantwortet nur die halbe Frage.** Sie sagt, wann die *Börse* eine Lizenz
verlangt. Unabhängig davon regelt der Vertrag des *Datenanbieters*, ob weiterverbreitet
werden darf — und kostenlose Zugänge erlauben das praktisch nie. Beides muss stimmen.

Deshalb holt `scripts/fetch_history.py` die Daten auf dem Rechner des Nutzers, statt
dass Parkett sie mitliefert: Schlüssel und Vertrag gehören dem Nutzer, Parkett
verbreitet nichts weiter. `.gitignore` nimmt `Parkett/Data/*.csv` deshalb aus — ein
Repository ist eine Weiterverbreitung. Nur das erfundene `DEMO.csv` ist eingecheckt.

**Für den Direktverkauf ist die Frage damit erledigt** — dort liegt nur `DEMO.csv` bei,
den Rest holt sich der Nutzer. Offen bleibt sie nur für eine auszuliefernde Fassung;
das ist mit Steam zurückgestellt und steht als Teilfrage bei Roadmap-Punkt 7.

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
- **Für eine TRX gibt es genau einen richtigen Schalter:** `--report-xunit-trx` hinter
  dem `--`-Separator, von xunit.v3 selbst, ohne Zusatzpaket. `--logger "trx;…"` ist ein
  VSTest-Flag: die Testing-Platform kennt es nicht, führt **null** Tests aus und endet
  rot, ohne dass ein Test fehlgeschlagen wäre. Nicht mit `--report-trx` verwechseln, das
  bräuchte `Microsoft.Testing.Extensions.TrxReport`. Die TRX landet in
  `TestResults/test-results.trx` im Repo-Root, nicht neben der Test-DLL.
- **Styles und Resource-Keys scheitern STILL.** Ein toter `Classes="accent"` oder ein
  fehlender `{DynamicResource XyzBrush}` gibt keinen Compile-Fehler — es rendert einfach
  falsch. `XamlResourceTests` macht daraus einen roten Testlauf: Key-Abgleich,
  Style-Klassen-Abgleich, keine Farbliterale außerhalb von `App.axaml`, keine doppelten
  `x:Key`. Alle vier wurden gegen absichtlich verbogenes XAML gegengeprüft — der Build
  blieb dabei grün, nur die Tests schlugen an. Genau deshalb gibt es sie.
- **Ein CI-Annotation-Schritt, der nichts findet, ist schlimmer als keiner.** Der Parser
  in `ci.yml` wurde einmal gegen einen absichtlich kaputten Test geprüft (PR #3, wieder
  verworfen) — er liefert Testname, Assertion und die Zeile im eigenen Testcode.
  Wer ihn anfasst, macht diese Gegenprobe erneut.
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

### Emoji im UI: der Font-Fallback ist Pflicht

Inter bringt keine Emoji-Glyphen mit. Ohne `FontManagerOptions.FontFallbacks` in
`BuildAvaloniaApp` rendern die Flaggen des Sprachumschalters (🇩🇪/🇬🇧) und die
Piktogramme der Transportleiste (▶ ⏸ ⏭) als Ersatzkästchen. Der Fallback zeigt
auf `Segoe UI Emoji` bzw. `Noto Color Emoji`.

**Falle dabei:** `WithInterFont()` setzt die Standardfamilie über dieselben Options.
Wer sie ersetzt, muss `DefaultFamilyName = "fonts:Inter#Inter"` erneut angeben —
sonst fällt die ganze App auf die System-Schrift zurück.

Bleibt: Windows rendert Regional-Indicator-Paare grundsätzlich nicht als Flaggen,
sondern als Buchstabenpaar (`DE`/`GB`) — das ist eine Entscheidung von Segoe UI Emoji,
kein Fehlen des Fonts. Unter Linux mit Noto Color Emoji sieht es richtig aus. Wer es
überall gleich haben will, muss kleine Flaggen-PNGs als Assets mitliefern.

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
| `Charting/EquityViewport` | Skalierung der Equity-Kurve, ohne Avalonia-Bezug und testbar |
| `ViewModels/ReportWindowViewModel` | Kennzahlen → Anzeigetexte und das Urteil in Worten |

### Der Abschlussbericht sagt mehr als die Kennzahlen

Drei Entscheidungen, die den Bericht von einer Zahlentabelle unterscheiden:

- **Die Gebührenlast steht vor dem Ergebnis.** Sie ist der Teil, den der Nutzer
  selbst steuert; das Marktergebnis nicht.
- **`ReturnWithoutFeesPercent` ist die eigentliche Lehre.** War die Sitzung nur
  wegen der Gebühren im Minus, bekommt sie ein eigenes Urteil
  (`Report_VerdictFeesAtePlus`) statt eines allgemeinen „leider verloren".
- **Das Startkapital ist in der Kurve immer sichtbar** (`EquityViewport`). Eine
  Kurve, die auf ihr eigenes Min/Max zoomt, sieht bei −40 % genauso aus wie bei
  +40 %. Die Fläche ist an dieser Linie zweifarbig geteilt — durchgehend eingefärbt
  wäre ein Verlauf, der lange im Plus lag und erst am Ende abrutscht, komplett rot.

**Randfälle, die real falsch aussahen und deshalb Tests haben:** Trefferquote ohne
abgeschlossenen Rundlauf (zeigt „—", nicht „0 %"), Punktlandung auf dem Startkapital
(eigenes Urteil, sonst stünde dort „du liegst darüber"), und die fortgesetzte Sitzung —
`TradingSession.Restore` baut die Equity-Kurve bewusst nicht nach, der Bericht weist
sich dann per `IsPartialSession` als Teilbericht aus.

### Warum das Hauptfenster-VM auf fünf Dateien liegt

`MainWindowViewModel` war 568 Zeilen lang; der Kroste-Standard zieht die Grenze bei
etwa 300. Aufgeteilt als partial class nach Zuständigkeit, nicht nach Zeilenzahl:

| Datei | Inhalt |
|---|---|
| `MainWindowViewModel.cs` | Felder, Konstruktor, gebundener Zustand |
| `.Session.cs` | Starten, Fortsetzen, Beenden, Stand sichern |
| `.Transport.cs` | Play/Pause, Einzelschritt, Tempo |
| `.Orders.cs` | Kaufen, Verkaufen, Stornieren |
| `.Presentation.cs` | Anzeigetexte und Sprachwechsel |

Neue Funktionen gehören in die passende Datei — nicht zurück in den Kern, sonst
wächst der binnen weniger Features wieder auf den alten Stand.
