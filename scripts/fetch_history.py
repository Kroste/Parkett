#!/usr/bin/env python3
"""
Holt EOD-Kurshistorie nach Parkett/Data/ — eine CSV je Symbol.

    python scripts/fetch_history.py alphavantage AAPL MSFT --key DEIN_KEY
    python scripts/fetch_history.py csv ~/Downloads/sap.csv --symbol SAP

WARUM DER NUTZER DIE DATEN HOLT UND NICHT PARKETT SIE MITLIEFERT
----------------------------------------------------------------
Historische Schlusskurse ab einem vollen Handelstag Alter brauchen keine
Börsenlizenz — das regelt aber nur die Seite der Börse. Der Vertrag des
Datenanbieters regelt zusätzlich, ob die Daten *weiterverbreitet* werden
dürfen, und das erlaubt praktisch kein kostenloser Zugang.

Deshalb dieses Skript statt eines Downloads im Produkt: der Schlüssel gehört
dem Nutzer, der Vertrag auch, und Parkett verbreitet nichts weiter. Dasselbe
Muster wie beim geplanten Alpaca-Provider.

Aus demselben Grund ist Stooq bewusst NICHT eingebaut, obwohl es die
bequemste Quelle wäre: dort sind keine Nutzungsbedingungen auffindbar, und
der CAPTCHA vor dem Bulk-Download deutet darauf hin, dass automatisiertes
Abrufen nicht vorgesehen ist. Wer die Daten von dort von Hand herunterlädt,
bringt sie über den csv-Modus herein — das ist die Entscheidung des Nutzers
und nicht die eines Skripts im Produkt.
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import date, datetime, timedelta
from pathlib import Path

# Die Windows-Konsole liefert cp1252, und dort lässt sich weder '←' noch ein
# Umlaut kodieren: das Skript stirbt sonst mitten in einer Statusmeldung an
# einem UnicodeEncodeError statt an einem echten Problem. errors="replace"
# als Netz, falls reconfigure auf einer exotischen Konsole nicht greift.
for _strom in (sys.stdout, sys.stderr):
    try:
        _strom.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError):  # pragma: no cover — sehr alte Umgebungen
        pass

DATA_DIR = Path(__file__).resolve().parent.parent / "Parkett" / "Data"

# Kopfzeile, die CsvHistoryProvider erwartet.
HEADER = ["Date", "Open", "High", "Low", "Close", "Volume"]

# Spaltennamen, die in freier Wildbahn vorkommen — deutsche Broker-Exporte
# eingeschlossen. Kleingeschrieben verglichen.
ALIASES = {
    "Date": ["date", "datum", "day", "time", "zeit", "handelstag"],
    "Open": ["open", "eröffnung", "eroeffnung", "erster", "first"],
    "High": ["high", "hoch", "höchst", "hoechst", "tageshoch"],
    "Low": ["low", "tief", "tiefst", "tagestief"],
    "Close": ["close", "schluss", "schlusskurs", "letzter", "last", "adj close", "close/last"],
    "Volume": ["volume", "volumen", "umsatz", "stück", "stueck"],
}


class DataError(Exception):
    """Fehler, den der Nutzer selbst beheben kann — wird ohne Stacktrace gemeldet."""


# ---------------------------------------------------------------- Lizenzregel


def last_allowed_day(today: date | None = None) -> date:
    """
    Jüngster Handelstag, der ausgeliefert werden darf: gestern, und über das
    Wochenende zurück auf den Freitag.

    Diese Regel steht bewusst im Skript und nicht nur in der Doku — sie ist
    die Grenze zwischen 'lizenzfrei' und 'braucht einen Vertrag mit der Börse'.
    """
    today = today or date.today()
    day = today - timedelta(days=1)

    while day.weekday() >= 5:  # 5 = Samstag, 6 = Sonntag
        day -= timedelta(days=1)

    return day


def drop_too_recent(rows: list[dict], today: date | None = None) -> tuple[list[dict], int]:
    """Schneidet zu junge Kerzen ab und meldet, wie viele es waren."""
    grenze = last_allowed_day(today)
    behalten = [r for r in rows if r["Date"] <= grenze]

    return behalten, len(rows) - len(behalten)


# ---------------------------------------------------------------- Zahlen/Daten


def parse_number(value: str) -> float:
    """
    Erkennt Punkt- und Komma-Dezimaltrennzeichen. Deutsche Exporte schreiben
    '1.234,56', englische '1,234.56' — wer nur das Komma ersetzt, macht aus
    dem englischen Tausendertrenner ein Dezimalkomma und verhundertfacht Kurse.
    """
    text = (value or "").strip().replace(" ", "").replace(" ", "")

    if not text or text in {"-", "--", "n/a", "N/A", "null"}:
        raise ValueError("leer")

    text = text.lstrip("$€£").rstrip("%")

    if "," in text and "." in text:
        # Das rechtere Zeichen ist das Dezimaltrennzeichen.
        if text.rfind(",") > text.rfind("."):
            text = text.replace(".", "").replace(",", ".")
        else:
            text = text.replace(",", "")
    elif text.count(",") > 1 or text.count(".") > 1:
        # Mehrfach vorhanden: Tausendertrenner — zwei Dezimalpunkte gibt es nicht.
        text = text.replace(",", "").replace(".", "")
    elif "," in text:
        # Genau einmal: Dezimalkomma. '1,234' wäre auch als Tausendertrenner
        # lesbar, aber ein Kurs ohne Nachkommastellen ist die Ausnahme — und
        # eine 3-Stellen-Heuristik machte aus '0,123' sonst 123.
        text = text.replace(",", ".")

    return float(text)


def parse_volume(value: str) -> int:
    """
    Volumen ist eine Stückzahl und damit ganzzahlig — hier ist ein einzelnes
    Trennzeichen mit drei Folgeziffern ein Tausendertrenner, nicht ein
    Dezimaltrenner. Genau umgekehrt zu :func:`parse_number`, wo '0,123' ein
    Kurs sein kann.

    Ohne diese Trennung wird aus einem Umsatz von '703.819' Stück eine 703.
    """
    text = (value or "").strip().replace(" ", "").replace("'", "")

    if not text:
        raise ValueError("leer")

    # Genau ein Trennzeichen, danach genau drei Ziffern -> Tausendertrenner.
    for zeichen in (".", ","):
        if text.count(zeichen) == 1 and text.count("." if zeichen == "," else ",") == 0:
            vorn, _, hinten = text.rpartition(zeichen)

            if len(hinten) == 3 and vorn.isdigit():
                return int(vorn + hinten)

    return int(round(parse_number(text)))


def parse_date(value: str) -> date:
    text = (value or "").strip()

    for muster in ("%Y-%m-%d", "%d.%m.%Y", "%m/%d/%Y", "%d/%m/%Y", "%Y/%m/%d", "%d-%m-%Y"):
        try:
            return datetime.strptime(text[:10], muster).date()
        except ValueError:
            continue

    raise ValueError(f"Datum nicht erkannt: {value!r}")


# ---------------------------------------------------------------- Alpha Vantage


def fetch_alphavantage(symbol: str, key: str) -> list[dict]:
    """
    TIME_SERIES_DAILY, volle Historie. Der freie Zugang erlaubt 25 Abrufe pro
    Tag — für ein Dutzend Instrumente reicht das, für einen Massenabzug nicht.
    """
    url = "https://www.alphavantage.co/query?" + urllib.parse.urlencode({
        "function": "TIME_SERIES_DAILY",
        "symbol": symbol,
        "outputsize": "full",
        "datatype": "json",
        "apikey": key,
    })

    try:
        with urllib.request.urlopen(url, timeout=60) as antwort:
            nutzdaten = json.loads(antwort.read().decode("utf-8"))
    except urllib.error.URLError as fehler:
        raise DataError(f"{symbol}: Abruf fehlgeschlagen ({fehler})") from fehler

    # Alpha Vantage antwortet auf Fehler mit HTTP 200 und einem Hinweistext —
    # ohne diese Prüfung landet eine Fehlermeldung als leere Kursdatei auf der Platte.
    for feld in ("Error Message", "Note", "Information"):
        if feld in nutzdaten:
            raise DataError(f"{symbol}: {nutzdaten[feld]}")

    reihe = nutzdaten.get("Time Series (Daily)")

    if not reihe:
        raise DataError(f"{symbol}: keine Zeitreihe in der Antwort")

    zeilen = []

    for tag, werte in reihe.items():
        try:
            zeilen.append({
                "Date": parse_date(tag),
                "Open": parse_number(werte["1. open"]),
                "High": parse_number(werte["2. high"]),
                "Low": parse_number(werte["3. low"]),
                "Close": parse_number(werte["4. close"]),
                "Volume": int(float(werte.get("5. volume", 0))),
            })
        except (KeyError, ValueError):
            continue

    return zeilen


# ---------------------------------------------------------------- CSV-Import


def sniff_delimiter(kopf: str) -> str:
    """Semikolon ist in deutschen Exporten der Normalfall, Tab kommt auch vor."""
    for kandidat in (";", "\t", ","):
        if kandidat in kopf:
            return kandidat

    return ","


def map_columns(feldnamen: list[str]) -> dict[str, str]:
    """Ordnet die Spalten der Datei den Parkett-Feldern zu."""
    vorhanden = {(name or "").strip().lower(): name for name in feldnamen}
    zuordnung = {}

    for ziel, kandidaten in ALIASES.items():
        for kandidat in kandidaten:
            if kandidat in vorhanden:
                zuordnung[ziel] = vorhanden[kandidat]
                break

    fehlend = [f for f in ("Date", "Close") if f not in zuordnung]

    if fehlend:
        raise DataError(
            f"Spalten nicht gefunden: {', '.join(fehlend)}. "
            f"Gefunden wurden: {', '.join(feldnamen)}"
        )

    return zuordnung


def read_csv(pfad: Path) -> list[dict]:
    """
    Liest eine fremde CSV. Fehlen OHLC-Spalten, wird der Schlusskurs eingesetzt:
    für einen EOD-Simulator ist das ehrlicher als erfundene Tagesspannen.
    """
    text = pfad.read_text(encoding="utf-8-sig", errors="replace")
    erste_zeile = text.splitlines()[0] if text.splitlines() else ""

    leser = csv.DictReader(io.StringIO(text), delimiter=sniff_delimiter(erste_zeile))

    if not leser.fieldnames:
        raise DataError(f"{pfad.name}: keine Kopfzeile gefunden")

    zuordnung = map_columns(list(leser.fieldnames))
    zeilen = []
    uebersprungen = 0

    for eintrag in leser:
        try:
            close = parse_number(eintrag[zuordnung["Close"]])

            zeile = {
                "Date": parse_date(eintrag[zuordnung["Date"]]),
                "Close": close,
                "Open": close,
                "High": close,
                "Low": close,
                "Volume": 0,
            }

            for feld in ("Open", "High", "Low"):
                if feld in zuordnung:
                    try:
                        zeile[feld] = parse_number(eintrag[zuordnung[feld]])
                    except (ValueError, TypeError):
                        pass

            if "Volume" in zuordnung:
                try:
                    zeile["Volume"] = parse_volume(eintrag[zuordnung["Volume"]])
                except (ValueError, TypeError):
                    pass

            zeilen.append(zeile)
        except (ValueError, KeyError, TypeError):
            uebersprungen += 1

    if uebersprungen:
        print(f"  {uebersprungen} Zeilen übersprungen (nicht lesbar)")

    return zeilen


# ---------------------------------------------------------------- Schreiben


def write_history(symbol: str, zeilen: list[dict], ziel: Path) -> Path:
    if not zeilen:
        raise DataError(f"{symbol}: keine verwertbaren Kurse")

    # Doppelte Tage können bei zusammengeführten Quellen auftreten — der letzte
    # Eintrag gewinnt, sonst zeigt der Chart zwei Kerzen am selben Tag.
    je_tag = {z["Date"]: z for z in zeilen}
    sortiert = [je_tag[t] for t in sorted(je_tag)]

    if len(sortiert) < 2:
        raise DataError(f"{symbol}: mindestens zwei Handelstage nötig, {len(sortiert)} gefunden")

    ziel.mkdir(parents=True, exist_ok=True)
    datei = ziel / f"{symbol}.csv"

    with datei.open("w", encoding="utf-8", newline="") as ausgabe:
        schreiber = csv.writer(ausgabe, lineterminator="\n")
        schreiber.writerow(HEADER)

        for zeile in sortiert:
            schreiber.writerow([
                zeile["Date"].isoformat(),
                f"{zeile['Open']:.4f}".rstrip("0").rstrip("."),
                f"{zeile['High']:.4f}".rstrip("0").rstrip("."),
                f"{zeile['Low']:.4f}".rstrip("0").rstrip("."),
                f"{zeile['Close']:.4f}".rstrip("0").rstrip("."),
                zeile["Volume"],
            ])

    spanne = f"{sortiert[0]['Date']} bis {sortiert[-1]['Date']}"
    print(f"  {datei.relative_to(Path.cwd()) if datei.is_relative_to(Path.cwd()) else datei}"
          f"  ({len(sortiert)} Handelstage, {spanne})")

    return datei


def process(symbol: str, zeilen: list[dict], ziel: Path) -> None:
    zeilen, zu_jung = drop_too_recent(zeilen)

    if zu_jung:
        print(f"  {zu_jung} Kerze(n) jünger als ein voller Handelstag — verworfen")

    write_history(symbol, zeilen, ziel)


# ---------------------------------------------------------------- Kommandozeile


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Holt EOD-Kurshistorie nach Parkett/Data/.",
        epilog="Der Zugang gehört dir, nicht Parkett — siehe Kopf dieser Datei.",
    )
    parser.add_argument("quelle", choices=["alphavantage", "csv"])
    parser.add_argument("werte", nargs="+", metavar="SYMBOL|DATEI")
    parser.add_argument("--key", help="API-Schlüssel (sonst aus ALPHAVANTAGE_KEY)")
    parser.add_argument("--symbol", help="Symbolname beim csv-Import (sonst Dateiname)")
    parser.add_argument("--out", type=Path, default=DATA_DIR, help=f"Zielordner (Standard: {DATA_DIR})")

    args = parser.parse_args(argv)
    fehler = 0

    if args.quelle == "alphavantage":
        key = args.key or os.environ.get("ALPHAVANTAGE_KEY")

        if not key:
            print("Kein Schlüssel. --key angeben oder ALPHAVANTAGE_KEY setzen.\n"
                  "Kostenlos unter https://www.alphavantage.co/support/#api-key", file=sys.stderr)
            return 2

        for symbol in args.werte:
            print(f"{symbol}:")
            try:
                process(symbol.upper(), fetch_alphavantage(symbol, key), args.out)
            except DataError as problem:
                print(f"  {problem}", file=sys.stderr)
                fehler += 1
    else:
        for eintrag in args.werte:
            pfad = Path(eintrag)
            symbol = (args.symbol or pfad.stem).upper()
            print(f"{symbol} ← {pfad.name}:")

            try:
                if not pfad.is_file():
                    raise DataError(f"Datei nicht gefunden: {pfad}")

                process(symbol, read_csv(pfad), args.out)
            except DataError as problem:
                print(f"  {problem}", file=sys.stderr)
                fehler += 1

    if fehler:
        print(f"\n{fehler} Symbol(e) fehlgeschlagen.", file=sys.stderr)

    return 1 if fehler else 0


if __name__ == "__main__":
    sys.exit(main())
