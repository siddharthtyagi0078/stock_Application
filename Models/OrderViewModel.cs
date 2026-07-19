namespace StockWebApplications.Models
{
    public class OrderViewModel
    {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string TransactionType { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal AveragePrice { get; set; }
        public string Status { get; set; }
        public string Exchange { get; set; }
        public DateTime? OrderTime { get; set; }
    }
}
