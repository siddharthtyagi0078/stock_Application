"""
Fetch fundamental data (PE, FV, ROE, Sector) from screener.in for every script
the app cares about, and repopulate dbo.ShareDetails.

Script universe:
    - dbo.shares       where sold = 0   (currently-held stocks)
    - dbo.sharetracker (ALL rows, incl. removed / target-achieved) so sold names
      still shown in reports (INFY, SBIN, MAZDOCK, ...) also get fundamentals
    unioned + de-duplicated on Script_code.

Source: https://www.screener.in/company/<symbol>/consolidated/  (falls back to the
standalone page). Ratios are matched by their LABEL ("Stock P/E", "Face Value",
"ROE") so screener re-ordering its top-ratios list can't shift the values.

Write model: exactly like the UpdateStockdetails proc — TRUNCATE ShareDetails
then INSERT the freshly scraped rows (updated = GETDATE()). The truncate is
skipped if nothing was scraped, so a total screener outage never wipes the table.

NOTE on "PV": ShareDetails has no PV column. Its columns are PE / FV / ROE / sector,
so the request's "PV" is stored in FV (Face Value), matching the existing app.

Setup (once):
    pip install -r requirements.txt
    # Needs ODBC Driver 17 for SQL Server.

Run:
    python fetch_share_details.py
"""

from __future__ import annotations

import re
import sys
import time
from datetime import datetime

import pyodbc
import requests
from lxml import html

# ----------------- CONFIG (edit if creds change) -----------------
SQL_CONN = (
    "DRIVER={ODBC Driver 17 for SQL Server};"
    "SERVER=103.21.58.192;"
    "DATABASE=ifutujah_paym;"
    "UID=puser;"
    "PWD=Timer@2711;"
    "Encrypt=no;"
)

# Polite scraping: pause between companies + timeout / retries per request.
REQUEST_TIMEOUT = 20          # seconds
DELAY_BETWEEN   = 1.2         # seconds between companies
MAX_RETRIES     = 2           # per URL, on network / rate-limit errors
RATE_LIMIT_SLEEP = 30         # seconds to back off on HTTP 429

HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"
    ),
    "Accept-Language": "en-US,en;q=0.9",
}
# -----------------------------------------------------------------


# Universe: every currently-held share (sold = 0) plus EVERY sharetracker row.
# sharetracker is intentionally unfiltered so that names removed from active
# tracking (display = 1) or already target-achieved still get fundamentals —
# e.g. INFY / SBIN / MAZDOCK, which are sold and display=1 but still shown in
# reports. (This is broader than dbo.st_Allscript, which only takes active ones.)
SCRIPTS_SQL = """
    SELECT Script_code, Script_name FROM dbo.shares       WHERE sold = 0
    UNION
    SELECT script_code, script_name FROM dbo.sharetracker
    ORDER BY 1
"""


def get_scripts(conn) -> list[tuple[str, str]]:
    """Return de-duplicated [(Script_code, name), ...] for shares(sold=0) + active tracker."""
    cur = conn.cursor()
    cur.execute(SCRIPTS_SQL)
    out, seen = [], set()
    for code, name in cur.fetchall():
        code = (code or "").strip()
        if not code or code.upper() in seen:
            continue
        seen.add(code.upper())
        out.append((code, (name or "").strip()))
    return out


def screener_symbol(code: str) -> str:
    """JIOFIN.NS -> JIOFIN, AVANCE.BO -> AVANCE (screener uses the bare symbol)."""
    return re.sub(r"\.(NS|BO)$", "", code, flags=re.IGNORECASE).strip().upper()


def _get(url: str) -> requests.Response | None:
    """GET with retries; returns the response or None. Backs off on HTTP 429."""
    for attempt in range(MAX_RETRIES):
        try:
            resp = requests.get(url, headers=HEADERS, timeout=REQUEST_TIMEOUT)
            if resp.status_code == 429:
                print(f"    rate-limited (429); sleeping {RATE_LIMIT_SLEEP}s")
                time.sleep(RATE_LIMIT_SLEEP)
                continue
            return resp
        except requests.RequestException as e:
            if attempt == MAX_RETRIES - 1:
                print(f"    request failed: {e}")
                return None
            time.sleep(2)
    return None


def _parse(tree) -> tuple[str, str, str, str]:
    """Pull (PE, FV, ROE, SECTOR) from a screener company page tree."""
    # Primary: match top-ratios by label so list re-ordering can't break us.
    ratios: dict[str, str] = {}
    for li in tree.xpath('//*[@id="top-ratios"]/li'):
        name = " ".join(li.xpath('.//span[contains(@class,"name")]//text()')).strip().lower()
        num = " ".join(li.xpath('.//span[contains(@class,"number")]//text()')).strip()
        if name:
            ratios[name] = num

    pe = ratios.get("stock p/e", "") or ratios.get("p/e", "")
    fv = ratios.get("face value", "")
    roe = ratios.get("roe", "")

    # Fallback to the fixed positions the old C# scraper used, if a label was missing.
    def pos(idx: int) -> str:
        n = tree.xpath(f'//*[@id="top-ratios"]/li[{idx}]/span[2]//span[contains(@class,"number")]//text()')
        return n[0].strip() if n else ""

    if not pe:
        pe = pos(4)
    if not roe:
        roe = pos(8)
    if not fv:
        fv = pos(9)

    sec_nodes = tree.xpath('//*[@id="peers"]/div[1]/div[1]/p[1]/a[1]//text()')
    sector = sec_nodes[0].strip() if sec_nodes else ""

    return pe, fv, roe, sector


def scrape(code: str) -> tuple[str, str, str, str]:
    """Scrape one company, filling gaps across the consolidated + standalone pages.

    Some companies (e.g. IRFC, MADRASFERT) leave the P/E, ROE and Face Value blank
    on /consolidated/ but populate them on the standalone page — so we can't stop at
    the first page just because it yielded a sector. We only stop early once all
    three ratios are in hand; otherwise we fetch the other page and fill the gaps.
    """
    sym = screener_symbol(code)
    pe = fv = roe = sector = ""
    for suffix in ("/consolidated/", "/"):
        url = f"https://www.screener.in/company/{sym}{suffix}"
        resp = _get(url)
        if resp is None or resp.status_code != 200:
            continue
        p, f, r, s = _parse(html.fromstring(resp.text))
        pe = pe or p
        fv = fv or f
        roe = roe or r
        sector = sector or s
        if pe and fv and roe:      # complete — no need for the second page
            break
    return pe, fv, roe, sector


def clip(v: str, n: int) -> str:
    v = (v or "").strip()
    return v[:n]


def write_to_db(conn, rows: list[tuple[str, str, str, str, str]]) -> None:
    """TRUNCATE + INSERT, mirroring UpdateStockdetails. Skips truncate if rows is empty."""
    if not rows:
        print("Nothing scraped — leaving ShareDetails untouched.")
        return
    cur = conn.cursor()
    cur.execute("TRUNCATE TABLE dbo.ShareDetails")
    cur.fast_executemany = True
    cur.executemany(
        """
        INSERT INTO dbo.ShareDetails (script_code, PE, FV, ROE, updated, sector)
        VALUES (?, ?, ?, ?, GETDATE(), ?)
        """,
        [
            (clip(c, 50), clip(pe, 10), clip(fv, 10), clip(roe, 10), clip(sec, 200))
            for (c, pe, fv, roe, sec) in rows
        ],
    )
    conn.commit()
    print(f"Wrote {len(rows)} rows to ShareDetails.")


def main() -> int:
    print(f"[{datetime.now():%Y-%m-%d %H:%M:%S}] connecting...")
    with pyodbc.connect(SQL_CONN, autocommit=False, timeout=15) as conn:
        scripts = get_scripts(conn)
        print(f"{len(scripts)} scripts to refresh (shares sold=0 + active tracker).")

        rows: list[tuple[str, str, str, str, str]] = []
        ok = 0
        for i, (code, name) in enumerate(scripts, 1):
            pe, fv, roe, sector = scrape(code)
            if pe or fv or roe or sector:
                ok += 1
            print(f"  [{i}/{len(scripts)}] {code:<15} PE={pe or '-':<7} FV={fv or '-':<6} "
                  f"ROE={roe or '-':<7} SECTOR={sector or '-'}")
            rows.append((code, pe, fv, roe, sector))
            time.sleep(DELAY_BETWEEN)

        print(f"Scraped {ok}/{len(rows)} with at least one value. Writing...")
        write_to_db(conn, rows)

    print("done.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)
