"""
Fetch NIFTY 1-min candles from Angel One SmartAPI, compute EMA10/EMA30 trend,
truncate the nifty_ema_daily table, and insert fresh rows.

Run once per morning (Windows Task Scheduler) from a machine whose IP is
whitelisted in Angel SmartAPI. The production web app just reads the table.

Setup (once):
    pip install -r requirements.txt
    # Install ODBC Driver 17 for SQL Server if not already:
    # https://learn.microsoft.com/sql/connect/odbc/download-odbc-driver-for-sql-server

Run:
    python fetch_nifty_ema.py
"""

from __future__ import annotations
import sys
import time
from datetime import datetime, timedelta

import pyotp
import pyodbc
from SmartApi import SmartConnect

# ----------------- CONFIG (edit if creds change) -----------------
API_KEY      = "oyCMiFNL"
CLIENT_CODE  = "CHND5002"
PIN          = "4995"
TOTP_SECRET  = "THCLWYPLO4SN3VMPOQFP6B2SMU"

NIFTY_TOKEN  = "99926000"   # NIFTY 50 index token on NSE
EXCHANGE     = "NSE"

SQL_CONN = (
    "DRIVER={ODBC Driver 17 for SQL Server};"
    "SERVER=103.21.58.192;"
    "DATABASE=ifutujah_paym;"
    "UID=puser;"
    "PWD=Concept@2711;"
    "Encrypt=no;"
)
# -----------------------------------------------------------------


def ema(values: list[float], period: int) -> list[float]:
    out = [0.0] * len(values)
    if not values:
        return out
    k = 2.0 / (period + 1.0)
    out[0] = values[0]
    for i in range(1, len(values)):
        out[i] = values[i] * k + out[i - 1] * (1.0 - k)
    return out


def login() -> SmartConnect:
    smart = SmartConnect(api_key=API_KEY)
    totp  = pyotp.TOTP(TOTP_SECRET).now()
    data  = smart.generateSession(CLIENT_CODE, PIN, totp)
    if not data or not data.get("status"):
        raise RuntimeError(f"Angel login failed: {data}")
    return smart


def fetch_candles(smart: SmartConnect) -> list[list]:
    """Returns raw candles: [[timestamp_str, open, high, low, close, volume], ...]"""
    today       = datetime.now().date()
    from_dt     = datetime.combine(today, datetime.min.time()) - timedelta(days=5)
    today_close = datetime.combine(today, datetime.min.time()) + timedelta(hours=15, minutes=30)
    to_dt       = min(datetime.now(), today_close)

    params = {
        "exchange":    EXCHANGE,
        "symboltoken": NIFTY_TOKEN,
        "interval":    "ONE_MINUTE",
        "fromdate":    from_dt.strftime("%Y-%m-%d %H:%M"),
        "todate":      to_dt.strftime("%Y-%m-%d %H:%M"),
    }
    resp = smart.getCandleData(params)
    if not resp or not resp.get("status"):
        raise RuntimeError(f"Candle fetch failed: {resp}")
    return resp.get("data") or []


def fetch_candles_with_retry() -> list[list]:
    """One retry with backoff on rate-limit errors from Angel."""
    for attempt in range(2):
        try:
            smart = login()
            return fetch_candles(smart)
        except Exception as e:
            if "rate" in str(e).lower() and attempt == 0:
                print("rate-limited by Angel; sleeping 30s then retrying.")
                time.sleep(30)
                continue
            raise
    return []


def build_rows(candles: list[list]) -> list[tuple]:
    """Filter to today's 09:15-15:30, compute EMA + trend, return DB rows."""
    parsed = []
    for c in candles:
        if not c or len(c) < 5:
            continue
        try:
            ts_str = c[0]
            # Angel returns ISO like '2026-07-20T09:15:00+05:30'
            ts = datetime.fromisoformat(ts_str.replace("Z", "+00:00"))
            close = float(c[4])
        except (ValueError, TypeError):
            continue
        parsed.append((ts, close))

    if not parsed:
        return []

    closes = [p[1] for p in parsed]
    e10    = ema(closes, 10)
    e30    = ema(closes, 30)

    today = datetime.now().date()
    day_open  = datetime.combine(today, datetime.min.time().replace(hour=9,  minute=15))
    day_close = datetime.combine(today, datetime.min.time().replace(hour=15, minute=30))

    rows = []
    prev_trend = None
    for i, (ts, cl) in enumerate(parsed):
        ts_naive = ts.replace(tzinfo=None)
        if ts_naive < day_open or ts_naive > day_close:
            prev_trend = None
            continue

        if e10[i] > e30[i]:
            trend = "SELL PE(Bullish)"
        elif e10[i] < e30[i]:
            trend = "SELL CE(Bearish)"
        else:
            trend = "-"

        is_cross = 1 if (prev_trend is not None and prev_trend != trend and trend != "-") else 0
        prev_trend = trend

        rows.append((
            ts_naive.strftime("%H:%M"),
            ts_naive,
            round(cl, 2),
            round(e10[i], 2),
            round(e30[i], 2),
            trend,
            is_cross,
        ))
    return rows


CREATE_TABLE_SQL = """
IF OBJECT_ID('dbo.nifty_ema_daily', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.nifty_ema_daily
    (
        id           INT IDENTITY(1,1) PRIMARY KEY,
        candle       VARCHAR(10)   NOT NULL,
        candle_ts    DATETIME      NOT NULL,
        [close]      DECIMAL(18,2) NOT NULL,
        ema10        DECIMAL(18,2) NOT NULL,
        ema30        DECIMAL(18,2) NOT NULL,
        trend        VARCHAR(40)   NOT NULL,
        is_cross     BIT           NOT NULL DEFAULT 0,
        updated_at   DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_nifty_ema_daily_ts ON dbo.nifty_ema_daily(candle_ts);
END
"""


def write_to_db(rows: list[tuple]) -> None:
    if not rows:
        print("No rows to write.")
        return
    with pyodbc.connect(SQL_CONN, autocommit=False) as conn:
        cur = conn.cursor()
        cur.execute(CREATE_TABLE_SQL)
        cur.execute("DELETE FROM dbo.nifty_ema_daily")
        deleted = cur.rowcount
        print(f"Cleared {deleted} old rows.")
        cur.fast_executemany = True
        cur.executemany(
            """
            INSERT INTO dbo.nifty_ema_daily
                (candle, candle_ts, [close], ema10, ema30, trend, is_cross)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            rows,
        )
        conn.commit()
    print(f"Inserted {len(rows)} rows.")


def main() -> int:
    print(f"[{datetime.now():%Y-%m-%d %H:%M:%S}] fetching candles...")
    candles = fetch_candles_with_retry()
    print(f"got {len(candles)} raw candles. computing EMA...")
    rows = build_rows(candles)
    print(f"computed {len(rows)} rows for today. writing to DB...")
    write_to_db(rows)
    print("done.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)
