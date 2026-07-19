using System.ComponentModel.DataAnnotations;

namespace StockWebApplications.Models
{
    public class OptionStrategyLegVM
    {
        public int StrategyLegId { get; set; }

        public int StrategyId { get; set; }

        public int LegNo { get; set; }

        [Required]
        public string ActionType { get; set; }

        [Required]
        public string InstrumentType { get; set; }

        public decimal? StrikePrice { get; set; }

        public DateTime? ExpiryDate { get; set; }

        [Required]
        public decimal TradePrice { get; set; }

        [Required]
        public int Quantity { get; set; }
    }
}
