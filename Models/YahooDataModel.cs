namespace StockWebApplications.Models
{
    public class YahooDataModel
    {
        // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
        public class Adjclose
        {
            public List<double> adjclose { get; set; }
        }

        public class Chart
        {
            public List<Result> result { get; set; }
            public object error { get; set; }
        }

        public class CurrentTradingPeriod
        {
            public Pre pre { get; set; }
            public Regular regular { get; set; }
            public Post post { get; set; }
        }

        public class Indicators
        {
            public List<Quote> quote { get; set; }
            public List<Adjclose> adjclose { get; set; }
        }

        public class Meta
        {
            public string currency { get; set; }
            public string symbol { get; set; }
            public string exchangeName { get; set; }
            public string fullExchangeName { get; set; }
            public string instrumentType { get; set; }
         //   public int firstTradeDate { get; set; }
            public int regularMarketTime { get; set; }
            public bool hasPrePostMarketData { get; set; }
            public int gmtoffset { get; set; }
            public string timezone { get; set; }
            public string exchangeTimezoneName { get; set; }
            public double regularMarketPrice { get; set; }
            public double fiftyTwoWeekHigh { get; set; }
            public double fiftyTwoWeekLow { get; set; }
            public double regularMarketDayHigh { get; set; }
            public double regularMarketDayLow { get; set; }
            public int regularMarketVolume { get; set; }
            public string longName { get; set; }
            public string shortName { get; set; }
            public double chartPreviousClose { get; set; }
            public int priceHint { get; set; }
            public CurrentTradingPeriod currentTradingPeriod { get; set; }
            public string dataGranularity { get; set; }
            public string range { get; set; }
            public List<string> validRanges { get; set; }
        }

        public class Post
        {
            public string timezone { get; set; }
            public int end { get; set; }
            public int start { get; set; }
            public int gmtoffset { get; set; }
        }

        public class Pre
        {
            public string timezone { get; set; }
            public int end { get; set; }
            public int start { get; set; }
            public int gmtoffset { get; set; }
        }

        public class Quote
        {
            public List<double> open { get; set; }
            public List<int> volume { get; set; }
            public List<double> close { get; set; }
            public List<double> high { get; set; }
            public List<double> low { get; set; }
        }

        public class Regular
        {
            public string timezone { get; set; }
            public int end { get; set; }
            public int start { get; set; }
            public int gmtoffset { get; set; }
        }

        public class Result
        {
            public Meta meta { get; set; }
            public List<int> timestamp { get; set; }
            public Indicators indicators { get; set; }
        }

        public class Root
        {
            public Chart chart { get; set; }
        }


    }
}
