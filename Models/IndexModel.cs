namespace StockWebApplications.Models
{
  
        public class MarketState
        {
            public string market { get; set; }
            public string marketStatus { get; set; }
            public DateTime tradeDate { get; set; }
            public string index { get; set; }
            public double last { get; set; }
            public double variation { get; set; }
            public double percentChange { get; set; }
            public string marketStatusMessage { get; set; }

        }
 
        public class Application
    {
            public IList<MarketState> marketState { get; set; }
         //   public Marketcap marketcap { get; set; }
         //   public Indicativenifty indicativenifty50 { get; set; }

        }
    }

