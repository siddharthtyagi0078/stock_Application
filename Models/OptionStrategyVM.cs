using System.ComponentModel.DataAnnotations;

namespace StockWebApplications.Models
{
    public class OptionStrategyVM
    {
        public OptionStrategyVM()
        {
            Legs = new List<OptionStrategyLegVM>();
            ExpiryDate = DateTime.Today;
        }

        public int StrategyId { get; set; }

        [Required]
        [Display(Name = "Strategy Name")]
        public string StrategyName { get; set; }

        [Required]
        public string Symbol { get; set; }

        [Required]
        [Display(Name = "Expiry Date")]
        [DataType(DataType.Date)]
        public DateTime ExpiryDate { get; set; }

        [Required]
        [Display(Name = "Lot Size")]
        public int LotSize { get; set; }

        public string Remarks { get; set; }

        public List<OptionStrategyLegVM> Legs { get; set; }
    }
}
