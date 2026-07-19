namespace StockWebApplications.Models
{
    public class StrategyDashboardVM
    {
        public List<StrategyMasterVM> Masters { get; set; }
            = new List<StrategyMasterVM>();

        public List<StrategyLegVM> Legs { get; set; }
            = new List<StrategyLegVM>();
    }
}
