-- Run once in SSMS against the production DB (ifutujah_paym).
IF OBJECT_ID('dbo.nifty_ema_daily', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.nifty_ema_daily
    (
        id           INT IDENTITY(1,1) PRIMARY KEY,
        candle       VARCHAR(10)   NOT NULL,   -- 'HH:mm'
        candle_ts    DATETIME      NOT NULL,   -- full timestamp for ordering
        [close]      DECIMAL(18,2) NOT NULL,
        ema10        DECIMAL(18,2) NOT NULL,
        ema30        DECIMAL(18,2) NOT NULL,
        trend        VARCHAR(40)   NOT NULL,
        is_cross     BIT           NOT NULL DEFAULT 0,
        updated_at   DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_nifty_ema_daily_ts ON dbo.nifty_ema_daily(candle_ts);
END
GO
