"""
Generates data/airline-callsigns.csv (ICAO airline designator -> radio telephony) from Wikidata.

Source: Wikidata, CC0 1.0 (public domain dedication). Properties:
  P230  ICAO airline designator
  P432  callsign of airline (radio telephony)
  P576  dissolved, abolished or demolished date (used to prefer operating airlines)
  wikibase:sitelinks  number of Wikipedia articles (used to break ties)

Usage (from the repository root):
  python tools/airline-callsigns/generate.py data/airline-callsigns.csv

Rules:
  - Only three-letter A-Z designators and telephonies made of letters, digits, spaces, hyphens and apostrophes.
  - When several entities share a designator, operating airlines (no P576) win over dissolved ones, then the
    entity with the most Wikipedia sitelinks wins (Wikidata often tags subsidiaries with the parent's code).
  - A designator still tied between different telephonies is left out (ambiguous), and listed on stderr.
"""
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import date

ENDPOINT = "https://query.wikidata.org/sparql"
QUERY = """
SELECT ?item ?icao ?callsign ?dissolved ?links WHERE {
  ?item wdt:P230 ?icao ;
        wdt:P432 ?callsign ;
        wikibase:sitelinks ?links .
  OPTIONAL { ?item wdt:P576 ?dissolved . }
}
"""
USER_AGENT = "OpenSkyATC-callsign-generator/1.0 (open-source GPL-3.0 flight simulator ATC add-on)"

ICAO = re.compile(r"^[A-Z]{3}$")
TELEPHONY = re.compile(r"^[A-Z0-9][A-Z0-9 '\-]*$")


def fetch(attempts=5):
    url = ENDPOINT + "?" + urllib.parse.urlencode({"query": QUERY, "format": "json"})
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/sparql-results+json"})
    for attempt in range(1, attempts + 1):
        try:
            with urllib.request.urlopen(req, timeout=300) as resp:
                return json.load(resp)["results"]["bindings"]
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == attempts:
                raise
            wait = int(e.headers.get("Retry-After", "90") or 90) + 5
            print(f"rate limited, waiting {wait} s (attempt {attempt}/{attempts})", file=sys.stderr)
            time.sleep(wait)


def main(out_path):
    rows = fetch()
    by_code = {}  # code -> list of (active, sitelinks, telephony)
    skipped_format = 0
    for r in rows:
        code = r["icao"]["value"].strip().upper()
        tel = " ".join(r["callsign"]["value"].strip().upper().split())
        if not ICAO.match(code) or not TELEPHONY.match(tel):
            skipped_format += 1
            continue
        by_code.setdefault(code, []).append(("dissolved" not in r, int(r["links"]["value"]), tel))

    table, ambiguous = {}, []
    for code, entries in sorted(by_code.items()):
        best = max((active, links) for active, links, _ in entries)
        winners = {tel for active, links, tel in entries if (active, links) == best}
        if len(winners) == 1:
            table[code] = next(iter(winners))
        else:
            ambiguous.append(f"{code}: {sorted(winners)}")

    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# ICAO airline designator to radio telephony.\n")
        f.write("# Source: Wikidata (https://www.wikidata.org), properties P230 and P432. Licence: CC0 1.0.\n")
        f.write(f"# Generated {date.today().isoformat()} by tools/airline-callsigns/generate.py; do not edit by hand.\n")
        f.write(f"# {len(table)} designators; {len(ambiguous)} ambiguous and {skipped_format} malformed entries left out.\n")
        for code, tel in table.items():
            f.write(f"{code},{tel}\n")

    print(f"{len(rows)} rows, {len(table)} designators written, {len(ambiguous)} ambiguous, {skipped_format} malformed", file=sys.stderr)
    for a in ambiguous:
        print("ambiguous " + a, file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1])
