# Parkett

[![CI](https://github.com/Kroste/Parkett/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/Parkett/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/Parkett)](https://github.com/Kroste/Parkett/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A stock market simulator with real prices and virtual money — a desktop app for
Windows and Linux (C# / .NET 10 / Avalonia 12).

Parkett trades against real historical price series, but strictly with play money.
It is a practice and learning tool: you see what your decisions do to a portfolio —
including the fees that make the difference in real trading.

> **No real money, no investment advice.** Parkett gives no buy or sell
> recommendations and is neither a broker nor a financial service provider.

<!-- Screenshot: add docs/screenshot.png once the UI has settled -->

## Features

- **Real price series:** trade against historical daily data instead of a random
  number generator.
- **Candlestick chart with a clock:** the session moves forward candle by candle —
  play, pause, single step and four speeds. The chart never shows a candle you
  could not have known about at the time of your decision.
- **Realistic execution:** buys at the ask, sells at the bid — never at the mid.
  Market, limit and stop orders, with a funds check that includes the fee.
- **Fees that hurt:** pick a fee model (neobroker, retail bank, or none). The
  running fee total sits permanently in the main window.
- **Metrics:** total value, realised result, maximum drawdown, hit rate after fees.
- **Resume a session:** the state is saved on exit and picked up at the same candle
  on the next start.
- **Bilingual:** English and German, switchable live without a restart.
- **Transparent data source:** the status bar always names the source and its delay.
- 🔄 **Self-update:** Parkett checks on startup (and any time via *About Parkett*)
  whether a newer version exists, downloads it on request, replaces itself and
  restarts. Nothing happens without your consent.

## Installation

Prebuilt packages are on the [releases page](https://github.com/Kroste/Parkett/releases):

**Windows:** download `Parkett-X.Y.Z-win-x64.zip`, extract it, run `Parkett.exe`.
No installation needed (self-contained, .NET is included).

**Linux (AppImage, recommended):**

```bash
chmod +x Parkett-*-x86_64.AppImage
./Parkett-*-x86_64.AppImage
```

**Linux (tar.gz):** extract `Parkett-X.Y.Z-linux-x64.tar.gz` and run `./Parkett`.

## Using it

1. **Pick an instrument and hit "New session"** — `DEMO` ships with the app, a made-up
   security for a first run. The chart starts with 60 candles of history so there is
   something to read.
2. **Let time run** — "Start" advances continuously, "Step" reveals exactly one candle.
   The speed selector sits next to it.
3. **Buy or sell** — set a quantity and click. Leave limit and stop empty and the order
   executes at market; with a limit or stop it goes into the book and waits for the
   price. If the cash is not enough, the status bar says so.
4. **Check the executions** — every fill lands in the list with price and fee, and as a
   triangle on the chart.
5. **End of session** — once the history has run out, a report opens: what the fees
   cost you, your portfolio value over time against your starting capital, the key
   figures, and a plain-language verdict. The status bar keeps the short version
   after you close it. **"Save report"** writes the whole report to a PNG at twice
   the screen resolution, so you can put two sessions side by side.

The portfolio starts with €10,000 of play money. Short selling is deliberately blocked.

The running session is saved on exit — "Resume" takes you back to the same candle on
the next start.

## Price data

Parkett reads prices from `Data/` (one CSV per symbol, `Date,Open,High,Low,Close,Volume`).
Only `DEMO.csv` with made-up prices ships with the app.

To trade real instruments, bring your own data with `scripts/fetch_history.py`:

```bash
# from a file you already have — broker export, spreadsheet, manual download
python scripts/fetch_history.py csv ~/Downloads/sap.csv --symbol SAP

# or through an API with your own key (free tier is enough for a dozen symbols)
export ALPHAVANTAGE_KEY=your_key
python scripts/fetch_history.py alphavantage AAPL MSFT
```

German exports are handled as they come — semicolons, comma decimals, `DD.MM.YYYY`,
column names like `Datum` / `Schlusskurs` / `Umsatz`.

**Why you fetch the data rather than Parkett shipping it:** historical closing prices
at least one full trading day old need no exchange license, but the *data provider's*
contract separately governs redistribution — and free tiers virtually never allow it.
So the key is yours, the contract is yours, and Parkett redistributes nothing. The
script drops any candle younger than a full trading day automatically. Details in
[`Parkett/Data/README.md`](Parkett/Data/README.md).

## Settings

Via the gear icon in the top right:

- **Language** — English or German, applied immediately in every window.
- **Fee model** — none, neobroker (€1.00 per order) or retail bank (€4.90 + 0.25 %,
  minimum €9.90). Running the same strategy under different models is where the actual
  lesson is.
- **License key** — for the Pro version sold directly.

Settings and session state live in `%APPDATA%\Parkett` or `~/.config/Parkett`.
The Pro license key is stored in:

- Windows: `%APPDATA%\Parkett\settings.json` (key encrypted via DPAPI)
- Linux: `~/.config/Parkett/settings.json` (key encrypted via AES-256-GCM)

## Logs and troubleshooting

Log files live in the user profile (daily archive, 14 days):

- Windows: `%APPDATA%\Parkett\logs\`
- Linux: `~/.config/Parkett/logs/`

If something goes wrong, please open an issue with the current log file — passwords,
tokens and license keys are masked automatically.

## Development

The app icon is generated from a script so it stays reproducible —
`scripts/build_icon.py` (Pillow) and `scripts/build_icon.ps1` (System.Drawing, no
Python dependency) produce the same result. Change both when the design changes.

```bash
dotnet build   # build
dotnet test    # tests (160 of them)
dotnet run --project Parkett
```

Release: VS Code task "release (tag + push)" — it checks the git state, sets the tag
and triggers the GitHub action that builds every package.

## License

MIT — see [LICENSE](LICENSE).
