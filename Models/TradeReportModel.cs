namespace StockWebApplications.Models
{
    public class TradeReportModel
    {
        public string Symbol { get; set; }
        public decimal? BuyPrice { get; set; }
        public decimal? SellPrice { get; set; }
        public int Quantity { get; set; }

        public decimal? ProfitLossValue { get; set; }
        public string ProfitLoss { get; set; }

        public string Status { get; set; }   // Profit / Loss / Open
        public string Type { get; set; }     // FUTURE / OPTION

        public string BuyOrderTime { get; set; }
        public string SellOrderTime { get; set; }
    }
}
