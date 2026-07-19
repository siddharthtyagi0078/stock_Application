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

        public List<StrategyLegVM> Legs { get; set; }
            = new List<StrategyLegVM>();
    }
}
