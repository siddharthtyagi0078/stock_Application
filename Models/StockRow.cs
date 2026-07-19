namespace StockWebApplications.Models
{
    public class StockRow
    {
        public Int64 SNo { get; set; }
        public string Name { get; set; }
        public int Qty { get; set; }
        public decimal Buy { get; set; }
        public decimal Sell { get; set; }
        public decimal TotalPL { get; set; }
        public string Date { get; set; }
        public string Duration { get; set; }
        public string Year { get; set; }
        public decimal perShare { get; set; }



    }
    public class DividendRow
    {
        public string Date { get; set; }
        public decimal Amount { get; set; }
    }

}
