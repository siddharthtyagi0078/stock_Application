namespace StockWebApplications.Models
{
    public class StockModel
    {
        public string companyName { get; set; }
        public DateTime dateTime { get; set; }
        public float open { get; set; }
        public float high { get; set; }
        public float low { get; set; }
        public float close { get; set; }
        public int volume { get; set; }
        public float adjustedClose { get; set; }
        public double change { get; set; }
        public double changepercent { get; set; }
        public string Sno { get; set; }
        public double FiftyTwoWeekLow { get; set; }
        public double FiftyTwoWeekHigh { get; set; }
        public string index_html { get; set; }
      
    }
    public class ProfitStockListResponse
    {
        public List<ProfitStockList> Stocklist { get; set; }
        public string Summary { get; set; }
    }

    public class PortfolioChart
    {
        public string date { get; set; }
 
        public string units { get; set; }
    }

    public class FIIData
    {

        public string Value { get; set; }
    }
        public class ProfitStockList
    {

        public string Name { get; set; }
        public decimal buyPrice { get; set; }
        public decimal SellPrice { get; set; }
        public decimal netProfit { get; set; }
        public string month_year { get; set; }
        public string Price_str { get; set; }
        public decimal perShare { get; set; }
        public string Sno { get; set; }
        public string Duration { get; set; }

        public string FyYear { get; set; }
        public string SoldOn { get; set; }
        public decimal profitpercent { get; set; }
        public decimal netProfitPercent { get; set; }
        public int shares { get; set; }
        public decimal Avg_percent { get; set; }

        public decimal dividend { get; set; }
        public string Account { get; set; }

    }
    public class StockList
    {
        public string Script_code { get; set; }
        public string Company_name { get; set; }
        public string Duration { get; set; }
        public int shares { get; set; }
        public int id { get; set; }
  public string compact_view { get; set; }
        public decimal inv_Price { get; set; }
        public int status { get; set; }
        public int user_id { get; set; }
        public DateTime DateAdded { get; set; }
        public StockModel stockModel { get; set; }
        public decimal latestValue { get; set; }
        public string latestValue_str { get; set; }
        public decimal daysGain { get; set; }
        public decimal overAllGain { get; set; }
        public string overAllGain_str { get; set; }
        public string Sno { get; set; }
        public string indicesText { get; set; }
        public double change { get; set; }
        public string changestr { get; set; }
        public string MMood { get; set; }
        public string VIX { get; set; }
        public List<StockModel> Indices { get; set; }
        public string VIX_val { get; set; }
        public string r_notes { get; set; }
        public string index { get; set; }
        public string MMOOdDesc { get; set; }
        public decimal diff { get; set; }
        public decimal monthly_profit { get; set; }
        public decimal dividend { get; set; }
        public string holiday { get; set; }


        public string stock_listing { get; set; }


        public string PE { get; set; }
        public string FV { get; set; }
        public string ROE { get; set; }
        public string FIIData { get; internal set; }
        public StrategyDashboardVM todayOrders {get; set; }
} 
   
    public class Nifty50
    {
        public string Sno { get; set; }
        public string Company_Name { get; set; }
        public string Industry { get; set; }
        public string Weight { get; set; }
        public StockModel stockModel { get; set; }
    }
    public class AddStock
    {
        public string Script_code { get; set; }
        public string DateAdded { get; set; }
        public string shares { get; set; }
        public string inv_Price { get; set; }
        public string Account { get; set; }

    }

    public class StockScript
    {
        public string Code { get; set; }
        public string Name { get; set; }

        public string Notes { get; set; }
       // public string Name { get; set; }
        public string Date { get; set; }

    }
    public class AddStocktracking
    {
        public string Script_code { get; set; }
        public string tgt_Date { get; set; }
      //  public string shares { get; set; }
        public string tgt_Price { get; set; }
        public string notes { get; set; }

    }

    public class AddStocktrackingReport
    {
        public int id { get; set; }
        public int diff { get; set; }
        public int display { get; set; }
        public string Sno { get; set; }
        public string Script_code { get; set; }
        public string Script_name { get; set; }
        public string Date_added { get; set; }
        public decimal tgt_Price { get; set; }
        public float current_Price { get; set; }
        public string current_Price_str { get; set; }
        public string notes { get; set; }
        public string days { get; set; }
        public string tgt_date { get; set; }
        public string r_notes { get; set; }
        public string index { get; set; }
        public string tgt_achieve_date { get; set; }
        public StockModel stockModel { get; set; }
        public string PE { get; set; }
        public string FV { get; set; }
        public string ROE { get; set; } 
        public string compact_view { get; set; }
        public string FIIData { get;  set; }
    }
    public class Reports
    {
        public string month { get; set; }
        public string year { get; set; }
        public string Script_code { get; set; }

    }

    public class StockResults
    {
        public string symbol { get; set; }
        public string company { get; set; }
        public string purpose { get; set; }
        public string bm_desc { get; set; }
        public string date { get; set; }
    }
}
