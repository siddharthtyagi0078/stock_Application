-- ============================================================
-- Adds legid to dbo.shares so an exited option leg can be joined
-- back to its OptionStrategyLeg row, and updates usp_ExitPosition
-- to write that legid on exit.
-- Run once against the production DB.
-- ============================================================

IF COL_LENGTH('dbo.shares', 'legid') IS NULL
BEGIN
    ALTER TABLE dbo.shares ADD legid INT NULL;
END
GO

CREATE INDEX IF NOT EXISTS IX_shares_legid ON dbo.shares(legid);
-- SQL Server < 2022 doesn't support CREATE INDEX IF NOT EXISTS; fallback below.
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_shares_legid' AND object_id = OBJECT_ID('dbo.shares'))
    CREATE INDEX IX_shares_legid ON dbo.shares(legid);
GO


ALTER PROCEDURE dbo.usp_ExitPosition
(
     @TradePrice DECIMAL(18,2),
     @legid  int
)
AS
BEGIN

SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRAN;

        DECLARE @ActionType     VARCHAR(10),
                @InstrumentType VARCHAR(10),
                @Strike         DECIMAL(18,2),
                @EntryPrice     DECIMAL(18,2),
                @Qty            INT,
                @Expiry         DATE,
                @Symbol         VARCHAR(30),
                @StrategyName   VARCHAR(100);

        SELECT  @ActionType     = L.ActionType,
                @InstrumentType = L.InstrumentType,
                @Strike         = L.StrikePrice,
                @EntryPrice     = L.TradePrice,
                @Qty            = L.Quantity,
                @Expiry         = L.ExpiryDate,
                @Symbol         = UPPER(S.Symbol),
                @StrategyName   = S.StrategyName
        FROM dbo.OptionStrategyLeg L
        INNER JOIN dbo.OptionStrategy S ON S.StrategyId = L.StrategyId
        WHERE L.StrategyLegId = @legid
          AND L.isactive = 1;

        IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK TRAN;
            RAISERROR('Leg not found or already exited.', 16, 1);
            RETURN;
        END

        DECLARE @ScriptCode VARCHAR(100) =
            @Symbol
            + CASE WHEN @InstrumentType = 'FUTURE'
                   THEN ' FUT'
                   ELSE ' ' + CAST(CAST(@Strike AS INT) AS VARCHAR(20)) + ' ' + @InstrumentType
              END
            + ' ' + FORMAT(@Expiry, 'ddMMMyy');

        IF @ScriptCode LIKE '%NIFTY%'
            SET @ScriptCode = 'NIFTY1.NS';
        ELSE
            SET @ScriptCode = UPPER(@ScriptCode) + '.NS';

        INSERT INTO dbo.shares
        (
            Script_code, shares, inv_Price, status, user_id, DateAdded,
            sell_price, sell_date, sold, Script_name,
            Dividend, active, charges, result_notes, compact_view, MYValid,
            legid
        )
        VALUES
        (
            @ScriptCode,
            @Qty,
            CASE WHEN @ActionType = 'SELL' THEN @TradePrice ELSE @EntryPrice END,
            0,
            1,
            GETDATE(),
            CASE WHEN @ActionType = 'SELL' THEN @EntryPrice ELSE @TradePrice END,
            GETDATE(),
            1,
            @StrategyName + ' - ' + @ActionType + ' ' + @InstrumentType,
            0,
            1,
            0,
            NULL,
            0,
            1,
            @legid
        );

        UPDATE dbo.OptionStrategyLeg
        SET isactive = 0
        WHERE StrategyLegId = @legid;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO
