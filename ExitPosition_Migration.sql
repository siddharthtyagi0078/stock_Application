-- ============================================================
-- usp_ExitPosition: books the exited leg into shares and
-- deactivates it so it stops displaying anywhere.
--
-- Price mapping (per requirement):
--   SELL leg          -> sell_price = leg entry TradePrice, inv_Price = @TradePrice (buy-back)
--   BUY / FUTURE leg  -> sell_price = @TradePrice (exit),   inv_Price = leg entry TradePrice
-- ============================================================

ALTER PROCEDURE dbo.usp_ExitPosition
(
     @TradePrice DECIMAL(18,2)  ,
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
        INNER JOIN dbo.OptionStrategy S
            ON S.StrategyId = L.StrategyId
        WHERE L.StrategyLegId = @legid
          AND L.isactive = 1;

        IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK TRAN;
            RAISERROR('Leg not found or already exited.', 16, 1);
            RETURN;
        END

        -- e.g. NIFTY 24100 PE 28Jul26  /  RELIANCE FUT 28Jul26
        DECLARE @ScriptCode VARCHAR(100) =
            @Symbol
            + CASE WHEN @InstrumentType = 'FUTURE'
                   THEN ' FUT'
                   ELSE ' ' + CAST(CAST(@Strike AS INT) AS VARCHAR(20)) + ' ' + @InstrumentType
              END
            + ' ' + FORMAT(@Expiry, 'ddMMMyy');

        -- Yahoo-style code for the shares table:
        -- anything NIFTY-based books under NIFTY1.NS, everything else UPPER + .NS
        IF @ScriptCode LIKE '%NIFTY%'
            SET @ScriptCode = 'NIFTY1.NS';
        ELSE
            SET @ScriptCode = UPPER(@ScriptCode) + '.NS';

        INSERT INTO dbo.shares
        (
            Script_code, shares, inv_Price, status, user_id, DateAdded,
            sell_price, sell_date, sold, Script_name,
            Dividend, active, charges, result_notes, compact_view, MYValid
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
            0,      -- Dividend
            1,      -- active (matches existing sold rows)
            0,      -- charges
            NULL,   -- result_notes
            0,      -- compact_view
            1       -- MYValid
        );

        -- Deactivate only after the shares entry succeeded
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
