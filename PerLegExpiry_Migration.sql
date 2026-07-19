-- ============================================================
-- FINAL STEP for per-leg expiry — run this against ifutujah_paym
--
-- Your table + insert proc changes are already done and verified.
-- This fixes usp_OptionStrategy_GetAll, which currently has 2 issues:
--   1) Result set 1 groups by L.ExpiryDate -> a strategy whose legs
--      span two expiries shows as DUPLICATE master rows.
--   2) Result set 2 (legs) does not return ExpiryDate -> per-leg
--      expiry never reaches the UI or the live-LTP fetch.
-- ============================================================

ALTER PROCEDURE usp_OptionStrategy_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    -------------------------------------------------------
    -- Result Set 1 (Master)
    -- One row per strategy; ExpiryDate = earliest leg expiry
    -------------------------------------------------------
    SELECT
        S.StrategyId,
        S.StrategyName,
        S.Symbol,
        MIN(L.ExpiryDate) AS ExpiryDate,
        S.LotSize,
        COUNT(L.LegNo) AS TotalLegs,

        SUM(
            CASE
                WHEN L.ActionType='SELL' THEN L.TradePrice
                ELSE 0
            END
        ) AS TotalPremium

    FROM OptionStrategy S
    INNER JOIN OptionStrategyLeg L
        ON S.StrategyId=L.StrategyId

    GROUP BY
        S.StrategyId,
        S.StrategyName,
        S.Symbol,
        S.LotSize

    ORDER BY S.StrategyId DESC;

    -------------------------------------------------------
    -- Result Set 2 (Children)
    -------------------------------------------------------
    SELECT
        StrategyLegId AS LegId,
        StrategyId,
        LegNo,
        ActionType,
        InstrumentType,
        StrikePrice,
        TradePrice,
        Quantity,
        ExpiryDate

    FROM OptionStrategyLeg
    ORDER BY StrategyId, LegNo;

END
