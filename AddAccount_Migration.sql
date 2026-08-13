/* ============================================================
   AddAccount_Migration.sql
   Adds an "Account" holder tag to stock buys (Shares) and to
   option strategies (OptionStrategy), so each transaction can be
   filtered by which account holds it.

   Allowed values (enforced in UI dropdown):
     Arnav-Angelone | Sid-Connect | Archana-Angelone

   Safe / backward compatible: new columns are NULLable and every
   new stored-proc parameter defaults to NULL.
   ============================================================ */

/* -------- 1. Columns -------- */
IF COL_LENGTH('dbo.Shares', 'Account') IS NULL
    ALTER TABLE dbo.Shares ADD Account VARCHAR(50) NULL;
GO

IF COL_LENGTH('dbo.OptionStrategy', 'Account') IS NULL
    ALTER TABLE dbo.OptionStrategy ADD Account VARCHAR(50) NULL;
GO

/* -------- 2. sp_insertStock (add @Account) -------- */
ALTER PROCEDURE [dbo].[sp_insertStock]
(
    @Script_code VARCHAR(50),
    @Script_name VARCHAR(500)=null,
    @DateAdded VARCHAR(15)=null,
    @shares INTEGER,
    @inv_Price decimal(18,2),
    @Account VARCHAR(50)=null
)
AS
BEGIN
    INSERT INTO Shares (Script_code, DateAdded, shares, inv_Price, status, user_id, [Script_name], charges, Dividend, active, sold, myvalid, Account)
    VALUES (upper(@Script_code), Convert(DATETIME, @DateAdded, 101), @shares, @inv_Price, 1, 1, @Script_name, 0, 0, 1, 0, 1, @Account);
END
GO

/* -------- 3. usp_OptionStrategy_Insert (add @Account) -------- */
ALTER PROCEDURE dbo.usp_OptionStrategy_Insert
(
    @StrategyName VARCHAR(100),
    @Symbol VARCHAR(30),
    @ExpiryDate DATE=null,
    @LotSize INT,
    @Remarks VARCHAR(max)=NULL,
    @Account VARCHAR(50)=NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OptionStrategy
    (
        StrategyName,
        Symbol,
        LotSize,
        Remarks,
        Account
    )
    VALUES
    (
        @StrategyName,
        @Symbol,
        @LotSize,
        @Remarks,
        @Account
    );

    SELECT SCOPE_IDENTITY() AS StrategyId;
END
GO

/* -------- 4. sp_getSoldStocks (return Account in the report) -------- */
ALTER procedure [dbo].[sp_getSoldStocks]
@year  int=0,
@scriptcode varchar(50)=null
as
begin
update shares set active=1

     declare @start_date as date
     declare @summary as varchar(max)

   declare @end_date as date
   if(@year=0)
   begin
      set @start_date= datefromparts(2022, 4, 1)
      set @end_date= datefromparts(2022+1, 3, 31)
   end
   else
   begin
      set @start_date= datefromparts(@year, 4, 1)
      set @end_date= datefromparts(@year+1, 3, 31)
   end

  declare @table as table
  (name varchar(300),buyPrice decimal(18,2)
  ,SellPrice decimal(18,2),perShare decimal(18,2)
  ,netProfit decimal(18,2),profitpercent decimal(18,2),shares int
  ,soldOn varchar(18),Duration varchar(50),FyYear varchar(20),DateAdded date,selldate date
  ,Account varchar(50)
  )

  insert into @table
select script_name[Name],inv_Price[buyPrice],sell_price[SellPrice],
(sell_price-inv_Price)[perShare],
((sell_price-inv_Price)*shares)++dbo.[getDividend](null,@year)[netProfit],
cast (round(((sell_price-inv_Price)/(inv_Price))*100,2) as float)[profitpercent]
,shares,FORMAT (sell_date, 'dd-MMM-yyyy') [SoldOn]
,dbo.getDuration([DateAdded],sell_date)'Duration'
, cast(year(sell_date) as varchar(10))+' - '+cast(year(sell_date)+1 as varchar(10)) FyYear  ,DateAdded,sell_date
,Account
from [dbo].[Shares]
where isnull(sold,0)=1 and active=1 and (@scriptcode is null or(Script_code=@scriptcode))
and(@year=0 or((sell_date>=@start_date)  and (sell_date<=@end_date)))
order by sell_date desc

declare @profitcount as int
declare @losscount as int
declare @stockcount as int
declare @totalcount as int
declare @maxprofitshare as varchar(100)
declare @maxlossshare as varchar(100)
declare @avgqty  AS int
declare @avgprofit  AS DECIMAL(7,2)
declare @dividend  AS DECIMAL(7,2)
declare @Profitpercent  AS DECIMAL(7,2)
declare @totalProfit  AS DECIMAL(17,2)
select @avgqty=avg (shares)   from @table
select @avgprofit=avg (netProfit)   from @table
select @Profitpercent=avg (Profitpercent) ,@totalProfit=sum(netProfit)  from @table
select @profitcount=count (name)   from @table where netprofit>0
select @losscount=count (name)   from @table where netprofit<=0
select top 1 @maxprofitshare=cast (netProfit as varchar)+'/- <br/>Script: [' + name  +']' from @table order by netProfit desc
select top 1 @maxlossshare=cast (netProfit as varchar)+'/- <br/>Script: [' + name+']'  from @table where  [netProfit]<0 order by netProfit
select @dividend=isnull(sum(dividend),0)from dividend where
(@year=0 or(([div_date]>=@start_date)  and ([div_date]<=@end_date)))
select @stockcount=count (name)   from @table where DATEDIFF(day,dateadded, soldOn )<=31
declare @successrate as decimal(18,2)
declare @color as varchar(5)
declare @color1 as varchar(5)

set @totalcount=@losscount+@profitcount

set   @successrate=0
if(@totalcount>0)
begin
    select @successrate= cast( round( CAST(@profitcount  AS DECIMAL(7,2))  /CAST((@totalcount) AS DECIMAL(7,2)) *100,2) as decimal(18,2))
end

select @color=( case when @successrate>50 then 'green' else 'red' end)
select @color1=( case when @avgprofit>0 then 'green' else 'red' end)

set @summary='<span style=''color:green'' >Total Profitable trade(s) :'+ cast( @profitcount as varchar)+'</span>
| <span style=''color:red'' >Total Loss trade(s):'+cast( @losscount as varchar)+'</span>'
+ '<br/ > <span style=''color:'+@color+''' > Success rate :'+ cast( @successrate as varchar) +'% </span>'
+ '<br/ >Stock hold Less than a Month :'+cast( @stockcount as varchar) +' out of ' +cast(@totalcount as varchar)
+ '<br/ >Average Quantity :'+cast( @avgqty as varchar) +' | <span style=''color:'+@color1+''' > Average Profit: ' + FORMAT (@avgprofit, 'c', 'en-US')   +'/- ('+cast(@Profitpercent as varchar) +' %)</span>'
+ '<br/ ><span style=''color:green'' >'+@maxprofitshare+'</span> <br/> <span style=''color:red'' >'+@maxlossshare+'</span>'

declare @fyyear as varchar(20)
set @fyyear=cast(@year as varchar)+' - '+cast(@year+1 as varchar)
if(@year=0)
begin
    set @fyyear='2023 - '+ cast( year(GETDATE())+1 as varchar)
end
select * from  @table  order by selldate desc
select @fyyear [fy_year],
isnull(@totalProfit,0)+@dividend[net_profit],
isnull(@totalProfit,0)[total_profit],
@dividend[dividend],
@totalcount[total_trades], @profitcount [profit_trade],@losscount[loss_trade],
@successrate[success_rate],@stockcount[hld_less1month],
isnull(@avgqty,0)[avgqty],
isnull(@avgprofit,0)[avg_profit],
isnull(@Profitpercent,0)[profit_percent],
isnull(@maxprofitshare,0)[max_profit],
isnull(@maxlossshare,0)[max_loss]
end
GO

/* -------- 5. usp_OptionStrategy_GetAll (return Account in master set) -------- */
ALTER PROCEDURE usp_OptionStrategy_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  distinct
        S.StrategyId,
        S.StrategyName,
        lower( S.Symbol)Symbol,
        l.ExpiryDate,
        S.LotSize,
        S.Account,
        COUNT(  LegNo) AS TotalLegs,
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
        l.ExpiryDate,
        S.LotSize,
        S.Account
    ORDER BY S.StrategyId DESC;

    SELECT
        strategylegid as LegId,
        StrategyId,
        LegNo,
        ActionType,
        InstrumentType,
        StrikePrice,
        TradePrice,
        Quantity  ,
        ExpiryDate
    FROM OptionStrategyLeg
    where isactive=1
    ORDER BY StrategyId,LegNo;
END
GO

/* -------- 6. usp_ExitPosition: carry the strategy's Account onto the
        Shares row created when a leg is exited (was inserting NULL) -------- */
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
                @StrategyName   VARCHAR(100),
                @Account        VARCHAR(50);

        SELECT  @ActionType     = L.ActionType,
                @InstrumentType = L.InstrumentType,
                @Strike         = L.StrikePrice,
                @EntryPrice     = L.TradePrice,
                @Qty            = L.Quantity,
                @Expiry         = L.ExpiryDate,
                @Symbol         = UPPER(S.Symbol),
                @StrategyName   = S.StrategyName,
                @Account        = S.Account
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
            legid, Account
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
            @legid,
            @Account
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
