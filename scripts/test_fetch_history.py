#!/usr/bin/env python3
"""
Selbsttest für fetch_history.py — ohne Netz, ohne Zusatzpakete.

    python scripts/test_fetch_history.py

Warum das hier steht und nicht im .NET-Testprojekt: die Zahlen- und
Datumserkennung ist der Teil, der still falsche Kurse erzeugt statt
abzustürzen. Ein '1,234', das als 1234 gelesen wird, verhundertfacht einen
Kurs — und im Chart sieht man nur, dass irgendetwas komisch aussieht.
"""

from __future__ import annotations

import sys
import tempfile
import unittest
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from fetch_history import (  # noqa: E402
    DataError,
    drop_too_recent,
    last_allowed_day,
    map_columns,
    parse_date,
    parse_number,
    parse_volume,
    read_csv,
    write_history,
)


class ZahlenTests(unittest.TestCase):
    def test_englisches_format(self):
        self.assertEqual(parse_number("1,234.56"), 1234.56)
        self.assertEqual(parse_number("123.45"), 123.45)

    def test_deutsches_format(self):
        self.assertEqual(parse_number("1.234,56"), 1234.56)
        self.assertEqual(parse_number("123,45"), 123.45)

    def test_einzelnes_komma_ist_ein_dezimalkomma(self):
        # Die Stelle, an der eine 3-Stellen-Heuristik danebengreift.
        self.assertEqual(parse_number("0,123"), 0.123)
        self.assertEqual(parse_number("1,234"), 1.234)

    def test_mehrfache_trenner_sind_tausendertrenner(self):
        self.assertEqual(parse_number("1,234,567"), 1234567.0)
        self.assertEqual(parse_number("1.234.567"), 1234567.0)

    def test_waehrungszeichen_und_leerzeichen(self):
        self.assertEqual(parse_number(" 123.45 "), 123.45)
        self.assertEqual(parse_number("$123.45"), 123.45)
        self.assertEqual(parse_number("€1.234,56"), 1234.56)

    def test_leere_werte_werfen(self):
        for wert in ("", "  ", "-", "n/a", "N/A", None):
            with self.assertRaises(ValueError):
                parse_number(wert)


class VolumenTests(unittest.TestCase):
    def test_tausendertrenner_bleibt_ganzzahlig(self):
        # Der Fall, der beim ersten echten Import danebenging: aus 703.819
        # Stück wurde eine 703.
        self.assertEqual(parse_volume("703.819"), 703819)
        self.assertEqual(parse_volume("703,819"), 703819)
        self.assertEqual(parse_volume("1.300.509"), 1300509)

    def test_ohne_trenner(self):
        self.assertEqual(parse_volume("1234567"), 1234567)

    def test_dezimalstellen_werden_gerundet(self):
        # Manche Quellen schreiben das Volumen mit Nachkommastellen.
        self.assertEqual(parse_volume("1234.0"), 1234)
        self.assertEqual(parse_volume("1.234,00"), 1234)


class DatumTests(unittest.TestCase):
    def test_formate(self):
        self.assertEqual(parse_date("2026-03-02"), date(2026, 3, 2))
        self.assertEqual(parse_date("02.03.2026"), date(2026, 3, 2))
        self.assertEqual(parse_date("2026-03-02 00:00:00"), date(2026, 3, 2))

    def test_unbekanntes_format_wirft(self):
        with self.assertRaises(ValueError):
            parse_date("gestern")


class LizenzregelTests(unittest.TestCase):
    def test_gestern_ist_die_grenze(self):
        # Mittwoch -> Dienstag
        self.assertEqual(last_allowed_day(date(2026, 3, 4)), date(2026, 3, 3))

    def test_ueber_das_wochenende_zurueck_auf_freitag(self):
        # Montag, 2026-03-02 -> Freitag, 2026-02-27 (nicht Sonntag)
        self.assertEqual(last_allowed_day(date(2026, 3, 2)), date(2026, 2, 27))
        # Sonntag -> Freitag
        self.assertEqual(last_allowed_day(date(2026, 3, 8)), date(2026, 3, 6))

    def test_zu_junge_kerzen_fliegen_raus(self):
        zeilen = [
            {"Date": date(2026, 3, 2)},
            {"Date": date(2026, 3, 3)},
            {"Date": date(2026, 3, 4)},  # heute
        ]

        behalten, verworfen = drop_too_recent(zeilen, today=date(2026, 3, 4))

        self.assertEqual(verworfen, 1)
        self.assertEqual([z["Date"] for z in behalten], [date(2026, 3, 2), date(2026, 3, 3)])


class SpaltenTests(unittest.TestCase):
    def test_englische_kopfzeile(self):
        zuordnung = map_columns(["Date", "Open", "High", "Low", "Close", "Volume"])
        self.assertEqual(zuordnung["Close"], "Close")

    def test_deutsche_kopfzeile(self):
        zuordnung = map_columns(["Datum", "Eröffnung", "Hoch", "Tief", "Schlusskurs", "Umsatz"])
        self.assertEqual(zuordnung["Date"], "Datum")
        self.assertEqual(zuordnung["Close"], "Schlusskurs")
        self.assertEqual(zuordnung["High"], "Hoch")

    def test_fehlende_pflichtspalte_wirft(self):
        with self.assertRaises(DataError):
            map_columns(["Datum", "Volumen"])


class CsvTests(unittest.TestCase):
    def _schreibe(self, inhalt: str) -> Path:
        ordner = Path(tempfile.mkdtemp())
        pfad = ordner / "quelle.csv"
        pfad.write_text(inhalt, encoding="utf-8")
        return pfad

    def test_deutscher_export_mit_semikolon(self):
        pfad = self._schreibe(
            "Datum;Eröffnung;Hoch;Tief;Schlusskurs;Umsatz\n"
            "02.03.2026;100,50;102,00;99,50;101,25;1.234.567\n"
            "03.03.2026;101,30;103,10;101,00;102,80;987.654\n"
        )

        zeilen = read_csv(pfad)

        self.assertEqual(len(zeilen), 2)
        self.assertEqual(zeilen[0]["Close"], 101.25)
        self.assertEqual(zeilen[0]["Volume"], 1234567)

    def test_nur_schlusskurse_fuellen_ohlc(self):
        # Ohne Tagesspanne ist der Schlusskurs ehrlicher als eine erfundene.
        pfad = self._schreibe("Date,Close\n2026-03-02,101.25\n2026-03-03,102.80\n")

        zeilen = read_csv(pfad)

        self.assertEqual(zeilen[0]["Open"], 101.25)
        self.assertEqual(zeilen[0]["High"], 101.25)
        self.assertEqual(zeilen[0]["Low"], 101.25)

    def test_kaputte_zeilen_werden_uebersprungen_nicht_abgebrochen(self):
        pfad = self._schreibe(
            "Date,Close\n2026-03-02,101.25\nMüll;;;\n2026-03-03,102.80\n"
        )

        self.assertEqual(len(read_csv(pfad)), 2)


class SchreibenTests(unittest.TestCase):
    def test_ausgabe_ist_sortiert_und_entdoppelt(self):
        ziel = Path(tempfile.mkdtemp())
        zeilen = [
            {"Date": date(2026, 3, 3), "Open": 1, "High": 1, "Low": 1, "Close": 102.8, "Volume": 5},
            {"Date": date(2026, 3, 2), "Open": 1, "High": 1, "Low": 1, "Close": 101.25, "Volume": 5},
            {"Date": date(2026, 3, 3), "Open": 1, "High": 1, "Low": 1, "Close": 103.5, "Volume": 9},
        ]

        datei = write_history("TEST", zeilen, ziel)
        text = datei.read_text(encoding="utf-8").splitlines()

        self.assertEqual(text[0], "Date,Open,High,Low,Close,Volume")
        self.assertEqual(len(text), 3, "der doppelte Tag darf nur einmal erscheinen")
        self.assertTrue(text[1].startswith("2026-03-02"))
        self.assertTrue(text[2].startswith("2026-03-03"))
        self.assertIn("103.5", text[2], "bei doppeltem Tag gewinnt der letzte Eintrag")

    def test_zu_kurze_historie_wirft(self):
        ziel = Path(tempfile.mkdtemp())
        zeilen = [{"Date": date(2026, 3, 2), "Open": 1, "High": 1, "Low": 1, "Close": 1, "Volume": 0}]

        with self.assertRaises(DataError):
            write_history("TEST", zeilen, ziel)

    def test_ausgabe_ist_vom_provider_lesbar(self):
        # Gegenprobe zum C#-Parser: Punkt als Dezimaltrenner, ISO-Datum, LF.
        ziel = Path(tempfile.mkdtemp())
        zeilen = [
            {"Date": date(2026, 3, 2), "Open": 100.5, "High": 102.0, "Low": 99.5,
             "Close": 101.25, "Volume": 1234567},
            {"Date": date(2026, 3, 3), "Open": 101.3, "High": 103.1, "Low": 101.0,
             "Close": 102.8, "Volume": 987654},
        ]

        datei = write_history("TEST", zeilen, ziel)
        roh = datei.read_bytes().decode("utf-8")

        self.assertNotIn("\r", roh, "CRLF bricht den Import auf Linux-Seite nicht, bleibt aber unnötig")
        self.assertIn("2026-03-02,100.5,102,99.5,101.25,1234567", roh)


if __name__ == "__main__":
    unittest.main(verbosity=2)
