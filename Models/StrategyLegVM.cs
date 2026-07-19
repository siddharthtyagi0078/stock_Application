namespace StockWebApplications.Models
{
    public class StrategyLegVM
    {
        public int LegId { get; set; }

        public int StrategyId { get; set; }

        public int LegNo { get; set; }

        public string ActionType { get; set; }

        public string InstrumentType { get; set; }

        public decimal? StrikePrice { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public decimal TradePrice { get; set; }

        public int Quantity { get; set; }
    }
}
