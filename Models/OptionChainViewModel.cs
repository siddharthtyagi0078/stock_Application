namespace StockWebApplications.Models
{
    public class OptionData
    {
        public decimal StrikePrice { get; set; }

        public decimal CallLTP { get; set; }
        public decimal CallChangePercent { get; set; }
        public decimal CallVolume { get; set; }

        public decimal PutLTP { get; set; }
        public decimal PutChangePercent { get; set; }
        public decimal PutVolume { get; set; }
    }

    public class OptionChainViewModel
    {
        public string IndexName { get; set; }
        public List<OptionData> Data { get; set; }

        public List<long> ExpiryDates { get; set; }
        public long SelectedExpiry { get; set; }

        public decimal SpotPrice { get; set; }

        public decimal MaxCallVolumeStrike { get; set; } // Resistance
        public decimal MaxPutVolumeStrike { get; set; }  // Support
    }
}
