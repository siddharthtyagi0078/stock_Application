namespace StockWebApplications.Models
{
    public class resultmodel
    {

        public class PageSummary
        {
            public string eventDate { get; set; }
            public int pageNo { get; set; }
            public int pageSize { get; set; }
            public string passedDate { get; set; }
            public int totalRecord { get; set; }
            public int totalPages { get; set; }
        }

        public class Root
        {
            public List<SearchResult> searchResult { get; set; }
            public PageSummary pageSummary { get; set; }
        }

        public class SearchResult
        {
            public string marketCap { get; set; }
            public string seoName { get; set; }
            public string @event { get; set; }
            public string name { get; set; }
            public string companyShortName { get; set; }
            public string dateSort { get; set; }
            public string companyType { get; set; }
            public string companyId { get; set; }
        }
    }
    public class Allscript {
        public string Script_code { get; set; }
        public string Script_name { get; set; }
    }

    public class Scriptcode {

        // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
        public class Data
        {
            public string suggestionTitleAccessor { get; set; }
            public List<string> suggestionMeta { get; set; }
            public bool hiConf { get; set; }
            public List<Item> items { get; set; }
        }

        public class Item
        {
            public string symbol { get; set; }
            public string name { get; set; }
            public string exch { get; set; }
            public string type { get; set; }
            public string exchDisp { get; set; }
            public string typeDisp { get; set; }
        }

        public class Meta
        {
        }

        public class Root
        {
            public Data data { get; set; }
            public Meta meta { get; set; }
        }


    }
}
