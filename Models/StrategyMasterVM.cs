namespace StockWebApplications.Models
{
    public class StrategyMasterVM
    {
        public int StrategyId { get; set; }

        public string StrategyName { get; set; }

        public string Symbol { get; set; }

        public DateTime ExpiryDate { get; set; }

        public int LotSize { get; set; }

        public int TotalLegs { get; set; }

        public decimal TotalPremium { get; set; }

        // true if the strategy has at least one active leg; false when all legs are closed.
        public bool IsActive { get; set; }

        // Sum of per-leg realized P/L for closed legs (from shares); active legs contribute 0.
        public decimal TotalPL { get; set; }

        public List<StrategyLegVM> Legs { get; set; }
            = new List<StrategyLegVM>();
    }
}
