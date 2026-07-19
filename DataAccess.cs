using HtmlAgilityPack;
using Org.BouncyCastle.Asn1.Ocsp;
using RestSharp;
using StockWebApplications.Models;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Web.Razor.Tokenizer;
using System.Xml;
using YahooFinanceApi;

namespace StockWebApplications
{
    public class DataAccess
    {
       string apiUrl = "https://query2.finance.yahoo.com/v8/finance/chart/{0}?interval=1d&events=history&crumb=7nRXFraYmVO";
        string ConnectioString = "Data Source=103.21.58.192;Initial Catalog=ifutujah_paym;Integrated Security = False; User ID = puser; Password=Concept@2711;Encrypt=False";



        public StrategyDashboardVM GetStrategies()
        {
            StrategyDashboardVM result = new StrategyDashboardVM();

            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand("usp_OptionStrategy_GetAll", con);
                cmd.CommandType = CommandType.StoredProcedure;

                SqlDataReader dr = cmd.ExecuteReader();

                //-------------------------------------------
                // Master
                //-------------------------------------------

                while (dr.Read())
                {
                    result.Masters.Add(new StrategyMasterVM
                    {
                        StrategyId = Convert.ToInt32(dr["StrategyId"]),
                        StrategyName = dr["StrategyName"].ToString(),
                        Symbol = dr["Symbol"].ToString(),

                        // NULL for pre-migration strategies whose legs carry no expiry
                        ExpiryDate = dr["ExpiryDate"] == DBNull.Value
                            ? DateTime.MinValue
                            : Convert.ToDateTime(dr["ExpiryDate"]),

                        LotSize = Convert.ToInt32(dr["LotSize"]),
                        TotalLegs = Convert.ToInt32(dr["TotalLegs"]),
                        TotalPremium = Convert.ToDecimal(dr["TotalPremium"])
                    });
                }

                //-------------------------------------------
                // Child
                //-------------------------------------------

                dr.NextResult();

                // ExpiryDate column exists only after the per-leg-expiry migration; read it when present.
                bool hasLegExpiry = false;
                for (int f = 0; f < dr.FieldCount; f++)
                {
                    if (string.Equals(dr.GetName(f), "ExpiryDate", StringComparison.OrdinalIgnoreCase))
                    {
                        hasLegExpiry = true;
                        break;
                    }
                }

                while (dr.Read())
                {
                    result.Legs.Add(new StrategyLegVM
                    {
                        LegId = Convert.ToInt32(dr["LegId"]),
                        StrategyId = Convert.ToInt32(dr["StrategyId"]),
                        LegNo = Convert.ToInt32(dr["LegNo"]),
                        ActionType = dr["ActionType"].ToString(),
                        InstrumentType = dr["InstrumentType"].ToString(),

                        StrikePrice = dr["StrikePrice"] == DBNull.Value
                            ? (decimal?)null
                            : Convert.ToDecimal(dr["StrikePrice"]),

                        ExpiryDate = hasLegExpiry && dr["ExpiryDate"] != DBNull.Value
                            ? Convert.ToDateTime(dr["ExpiryDate"])
                            : (DateTime?)null,

                        TradePrice = Convert.ToDecimal(dr["TradePrice"]),
                        Quantity = Convert.ToInt32(dr["Quantity"])
                    });
                }

                dr.Close();
            }

            //-------------------------------------------
            // Merge Parent + Child
            //-------------------------------------------
            // The proc groups masters by leg expiry, so a strategy whose legs
            // span multiple expiries comes back as multiple rows. De-duplicate
            // per StrategyId and recompute the aggregates from the (active)
            // legs the proc returned. Strategies with no active legs are hidden.

            var mergedMasters = new List<StrategyMasterVM>();

            foreach (var group in result.Masters.GroupBy(m => m.StrategyId))
            {
                var master = group.First();

                master.Legs = result.Legs
                    .Where(x => x.StrategyId == master.StrategyId)
                    .OrderBy(x => x.LegNo)
                    .ToList();

                if (master.Legs.Count == 0) continue;

                master.TotalLegs = master.Legs.Count;

                master.TotalPremium = master.Legs
                    .Where(l => string.Equals(l.ActionType, "SELL", StringComparison.OrdinalIgnoreCase))
                    .Sum(l => l.TradePrice);

                var expiries = master.Legs
                    .Where(l => l.ExpiryDate.HasValue)
                    .Select(l => l.ExpiryDate.Value)
                    .ToList();

                if (expiries.Count > 0)
                    master.ExpiryDate = expiries.Min();

                mergedMasters.Add(master);
            }

            result.Masters = mergedMasters;

            return result;
        }
        public void ExitPosition(int legId, decimal exitPrice)
        {
            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand("usp_ExitPosition", con);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@legid", legId);
                cmd.Parameters.AddWithValue("@TradePrice", exitPrice);

                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteStrategyLeg(int legId)
        {
            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(
                    "DELETE FROM OptionStrategyLeg WHERE StrategyLegId=@LegId", con);

                cmd.Parameters.AddWithValue("@LegId", legId);

                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateStrategyRemarks(int strategyId, string remarks)
        {
            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(
                    "UPDATE OptionStrategy SET Remarks=@Remarks WHERE StrategyId=@StrategyId", con);

                cmd.Parameters.AddWithValue("@StrategyId", strategyId);
                cmd.Parameters.AddWithValue("@Remarks",
                    string.IsNullOrWhiteSpace(remarks) ? (object)DBNull.Value : remarks);

                cmd.ExecuteNonQuery();
            }
        }

        public string GetStrategyRemarks(int strategyId)
        {
            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(
                    "SELECT Remarks FROM OptionStrategy WHERE StrategyId=@StrategyId", con);

                cmd.Parameters.AddWithValue("@StrategyId", strategyId);

                var val = cmd.ExecuteScalar();

                return val == null || val == DBNull.Value ? "" : val.ToString();
            }
        }

        //-------------------------------------------------------------
        // Insert Parent
        //-------------------------------------------------------------
        public int InsertStrategy(OptionStrategyVM model)
        {
            int strategyId = 0;

            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand com = new SqlCommand("usp_OptionStrategy_Insert", con);

                com.CommandType = CommandType.StoredProcedure;

                com.Parameters.AddWithValue("@StrategyName", model.StrategyName);
                com.Parameters.AddWithValue("@Symbol", model.Symbol);
                com.Parameters.AddWithValue("@ExpiryDate", model.ExpiryDate);
                com.Parameters.AddWithValue("@LotSize", model.LotSize);
                com.Parameters.AddWithValue("@Remarks",
                    string.IsNullOrWhiteSpace(model.Remarks)
                        ? (object)DBNull.Value
                        : model.Remarks);

                strategyId = Convert.ToInt32(com.ExecuteScalar());
            }

            return strategyId;
        }

        //-------------------------------------------------------------
        // Insert Child
        //-------------------------------------------------------------
        public int InsertStrategyLeg(OptionStrategyLegVM model)
        {
            int i = 0;

            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();

                SqlCommand com = new SqlCommand("usp_OptionStrategyLeg_Insert", con);

                com.CommandType = CommandType.StoredProcedure;

                com.Parameters.AddWithValue("@StrategyId", model.StrategyId);
                com.Parameters.AddWithValue("@LegNo", model.LegNo);
                com.Parameters.AddWithValue("@ActionType", model.ActionType);
                com.Parameters.AddWithValue("@InstrumentType", model.InstrumentType);

                if (model.StrikePrice == null)
                    com.Parameters.AddWithValue("@StrikePrice", DBNull.Value);
                else
                    com.Parameters.AddWithValue("@StrikePrice", model.StrikePrice);

                if (model.ExpiryDate == null)
                    com.Parameters.AddWithValue("@ExpiryDate", DBNull.Value);
                else
                    com.Parameters.AddWithValue("@ExpiryDate", model.ExpiryDate);

                com.Parameters.AddWithValue("@TradePrice", model.TradePrice);
                com.Parameters.AddWithValue("@Quantity", model.Quantity);

                i = com.ExecuteNonQuery();
            }

            return i;
        }

        public int AddStock(AddStock objstock)
        {
            int i;
            objstock.Script_code = objstock.Script_code.ToUpper();
            if (!objstock.Script_code.ToUpper().EndsWith("NS"))
            {
                objstock.Script_code = objstock.Script_code.ToUpper() + ".NS";
            }

            var script = GetStockLive(objstock.Script_code);


            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();
                SqlCommand com = new SqlCommand("sp_insertStock", con);
                com.CommandType = CommandType.StoredProcedure;
                com.Parameters.AddWithValue("@Script_code", objstock.Script_code);
                com.Parameters.AddWithValue("@Script_name", script.Result.companyName);

                com.Parameters.AddWithValue("@DateAdded", objstock.DateAdded);
                com.Parameters.AddWithValue("@shares", objstock.shares);
                com.Parameters.AddWithValue("@inv_Price", Convert.ToDecimal(objstock.inv_Price));
                i = com.ExecuteNonQuery();
            }
            return i;
        }
        public int AddStocktracking(AddStocktracking objstock, int userid = 1)
        {
            int i;
            objstock.Script_code = objstock.Script_code.ToUpper();
            if (!objstock.Script_code.ToUpper().EndsWith("NS"))
            {
                objstock.Script_code = objstock.Script_code.ToUpper() + ".NS";
            }

            var script = GetStockLive(objstock.Script_code);

            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();
                SqlCommand com = new SqlCommand("sp_insertStockTracking", con);
                com.CommandType = CommandType.StoredProcedure;
                com.Parameters.AddWithValue("@Script_code", objstock.Script_code);
                com.Parameters.AddWithValue("@userid", userid);
                com.Parameters.AddWithValue("@Script_name", script.Result.companyName);

                com.Parameters.AddWithValue("@notes", objstock.notes);
                com.Parameters.AddWithValue("@tgt_date", objstock.tgt_Date);
                com.Parameters.AddWithValue("@tgt_price", Convert.ToDecimal(objstock.tgt_Price));
                i = com.ExecuteNonQuery();
            }
            return i;
        }
        public List<StockList> GetStockDetails(out decimal profitval, out decimal dividend
            , out string Holiday
            )
        {
            List<StockList> lststockModel = new List<StockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_getStocks", con);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            DataSet myrec = new DataSet();
            try
            {


                da.Fill(myrec);
                con.Close();
                profitval = 0;
                dividend = 0;
                Holiday = "N/A";
                if (myrec.Tables[1].Rows.Count > 0)
                {
                    profitval = (decimal)myrec.Tables[1].Rows[0][1];
                }
                if (myrec.Tables[2].Rows.Count > 0)
                {
                    dividend = (Int32)myrec.Tables[2].Rows[0][0];
                }

                if (myrec.Tables[3].Rows.Count > 0)
                {
                    Holiday = myrec.Tables[3].Rows[0][0].ToString();
                }

                foreach (DataRow dr in myrec.Tables[0].Rows)
                {
                    lststockModel.Add(new StockList
                    {
                        Script_code = Convert.ToString(dr["Script_code"]),
                        shares = Convert.ToInt32(dr["shares"]),
                        id = Convert.ToInt32(dr["id"]),
                        inv_Price = Convert.ToDecimal(dr["inv_Price"]),
                        status = Convert.ToInt32(dr["status"]),
                        user_id = Convert.ToInt32(dr["user_id"]),
                        stockModel = GetStockLive(Convert.ToString(dr["Script_code"])).Result,
                        Duration = Convert.ToString(dr["Duration"]),
                        r_notes = Convert.ToString(dr["r_notes"]),
                        index = Convert.ToString(dr["index"]),
                        ROE = Convert.ToString(dr["ROE"]),
                        PE = Convert.ToString(dr["PE"]),
                        FV = Convert.ToString(dr["FV"]),
                        compact_view = Convert.ToString(dr["compact_view"]),
                        FIIData = Convert.ToString(dr["FII"])


                    });
                }

            }
            catch (Exception ex) { throw ex; }
            return lststockModel;
        }
        public List<AddStocktrackingReport> GetStocktrackingDetails(string val)
        {
            List<AddStocktrackingReport> lststockModel = new List<AddStocktrackingReport>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_insertStockTrackingreport", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@val", val);

            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            DataSet myrec = new DataSet();
            try
            {
                da.Fill(myrec);
                //con.Close();
                foreach (DataRow dr in myrec.Tables[0].Rows)
                {
                    lststockModel.Add(new AddStocktrackingReport
                    {
                        Script_name = Convert.ToString(dr["Script_name"]),
                        id = Convert.ToInt32(dr["id"]),
                        Date_added = Convert.ToString(dr["date_added"].ToString().Replace("12:00:00 AM", "")),
                        tgt_Price = Convert.ToDecimal(dr["tgt_Price"]),
                        tgt_date = dr["tgt_date"].ToString().Replace("12:00:00 AM", ""),
                        notes = dr["notes"].ToString(),
                        stockModel = GetStockLive(Convert.ToString(dr["Script_code"])).Result,
                        tgt_achieve_date = Convert.ToString(dr["tgt_achieve_date"]),
                        r_notes = Convert.ToString(dr["r_notes"]),
                        index = Convert.ToString(dr["index"]),
                        display = Convert.ToInt16(dr["display"]),
                        ROE = Convert.ToString(dr["ROE"]),
                        PE = Convert.ToString(dr["PE"]),
                        FV = Convert.ToString(dr["FV"]),
                        compact_view = Convert.ToString(dr["compact_view"]),
                        FIIData = Convert.ToString(dr["FII"])


                    });
                }
            }
            finally
            {
                con.Close();
            }
            return lststockModel;
        }
        public List<FIIData> GetFIIData()
        {
            List<FIIData> lststockModel = new List<FIIData>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("getFIIDataDaily", con);

            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            DataSet myrec = new DataSet();
            try
            {

                da.Fill(myrec);

                foreach (DataRow dr in myrec.Tables[0].Rows)
                {
                    lststockModel.Add(new FIIData
                    {
                        Value = Convert.ToString(dr["dataval"])
                    });
                }
            }
            finally
            {
                con.Close();
            }
            return lststockModel;
        }

        public List<ProfitStockList> GetProfitStockDetails(string year,string scriptcode, out DataTable summary)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            //  summary = "";
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_getSoldStocks", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@year", year);
            if (!string.IsNullOrEmpty( scriptcode))
            {
                if (!scriptcode.EndsWith("ns"))
                {
                    scriptcode= scriptcode.ToUpper() + ".NS";
                }
                cmd.Parameters.AddWithValue("@scriptcode", scriptcode.ToUpper());

            }
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            DataSet myrec = new DataSet();
            try
            {

                da.Fill(myrec);
                summary = myrec.Tables[1];
                //if (!string.IsNullOrEmpty(summary))
                //{
                //    summary = summary.Replace("$", "₹");
                //}
                foreach (DataRow dr in myrec.Tables[0].Rows)
                {
                    lststockModel.Add(new ProfitStockList
                    {
                        Name = Convert.ToString(dr["Name"]),
                        shares = Convert.ToInt32(dr["shares"]),
                        buyPrice = Convert.ToDecimal(dr["buyPrice"]),
                        SellPrice = Convert.ToDecimal(dr["SellPrice"]),
                        netProfit = Convert.ToDecimal(dr["netProfit"]),
                        perShare = Convert.ToDecimal(dr["perShare"]),
                        SoldOn = Convert.ToString(dr["SoldOn"]),
                        profitpercent = Convert.ToDecimal(dr["profitpercent"]),
                        Duration = Convert.ToString(dr["Duration"]),
                        FyYear = Convert.ToString(dr["FyYear"])



                    });
                }
            }
            finally
            {
                con.Close();
            }
            return lststockModel;
        }
        public List<ProfitStockList> GetProfitStockReport(string year, string month)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_getSoldStocksreport", con);
            if (year.Trim() != "0")
            {
                cmd.Parameters.AddWithValue("@year", year);
            }

            if (month.Trim() != "0")
            {
                cmd.Parameters.AddWithValue("@month", month);
            }
            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            DataSet myrec = new DataSet();
            try
            {

                da.Fill(myrec);

                foreach (DataRow dr in myrec.Tables[0].Rows)
                {
                    lststockModel.Add(new ProfitStockList
                    {
                        month_year = Convert.ToString(dr["month_year"]),
                        netProfit = Convert.ToDecimal(dr["netProfit"]),
                        Avg_percent = Convert.ToDecimal(dr["Avg_percent"]),
                        dividend = Convert.ToDecimal(dr["dividend"])
                    });
                }
            }
            finally
            {
                con.Close();
            }
            return lststockModel;
        }
        private StockModel GetStockLive_workingone(string symbol)
        {
            //var client = new RestClient("https://localhost:7261/api/Stock/JIOFIN.NS");
            var client = new RestClient(String.Format("", symbol));
            //  https://query2.finance.yahoo.com/v8/finance/chart/JIOFIN.NS?period1=1725696000&period2=1725868800&interval=1d&events=history&crumb=7nRXFraYmVO
            var request = new RestRequest();
            var response = client.Execute(request);
            var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            YahooDataModel.Root data = new YahooDataModel.Root();
            StockModel data2 = new StockModel();

            try
            {
                data = JsonSerializer.Deserialize<YahooDataModel.Root>(response.Content, options);
                if (data.chart.result.Count > 0)
                {
                    var dataval = data.chart.result[0].meta;
                    var dquote = data.chart.result[0].indicators.quote[0];

                    data2.companyName = dataval.longName;
                    // data2.dateTime = dataval.
                    data2.open = (float)dquote.open.FirstOrDefault();
                    data2.high = (float)dquote.high.FirstOrDefault();
                    data2.low = (float)dquote.low.FirstOrDefault();
                    data2.close = (float)dataval.regularMarketPrice;
                    data2.volume = dquote.volume.FirstOrDefault();
                    data2.adjustedClose = (float)dataval.regularMarketPrice;
                    var prev_close = dataval.chartPreviousClose;

                    data2.change = Math.Round(dataval.regularMarketPrice - prev_close, 2);
                    data2.changepercent = Math.Round((data2.change / prev_close) * 100, 2);
                    data2.FiftyTwoWeekLow = dataval.fiftyTwoWeekLow;
                    data2.FiftyTwoWeekHigh = dataval.fiftyTwoWeekHigh;
                    //data.chart.result[0].meta
                }

            }
            catch (Exception ex)
            {


            }

            return data2;
        }
        private async Task<StockModel> GetStockLive(string symbol)
        {
            StockModel data2 = new StockModel();
            try
            {

                var securities = await Yahoo.Symbols(symbol).Fields(Field.Symbol, Field.RegularMarketPrice,
    Field.RegularMarketChange, Field.RegularMarketChangePercent, Field.RegularMarketOpen, Field.DividendDate, Field.FiftyTwoWeekLow,
    Field.FiftyTwoWeekHigh, Field.ShortName, Field.RegularMarketVolume, Field.RegularMarketDayHigh, Field.RegularMarketDayLow).QueryAsync();
                var aapl = securities[symbol];
                // var price = aapl[Field.RegularMarketPrice];


                if (aapl != null)
                {
                    data2.companyName = aapl[Field.ShortName];
                    data2.open = (float)aapl[Field.RegularMarketOpen];
                    data2.high = (float)aapl[Field.RegularMarketDayHigh];
                    data2.low = (float)aapl[Field.RegularMarketDayLow];
                    data2.close = (float)aapl[Field.RegularMarketPrice];
                    data2.volume = (int)aapl[Field.RegularMarketVolume];
                    data2.adjustedClose = (float)aapl[Field.RegularMarketPrice];

                    data2.change = Math.Round(aapl[Field.RegularMarketChange], 2);
                    data2.changepercent = aapl[Field.RegularMarketChangePercent];
                    data2.FiftyTwoWeekLow = aapl[Field.FiftyTwoWeekLow];
                    data2.FiftyTwoWeekHigh = aapl[Field.FiftyTwoWeekHigh];
                    data2.FiftyTwoWeekHigh = aapl[Field.FiftyTwoWeekHigh];

                    //Field.
                }

            }
            catch (Exception ex)
            {


            }

            return data2;
        }
        public string FormatNumber(int num, out int count)
        {
            string val = "0";
            count = 0;
            if (num >= 10000000)
            {
                val = (num / 10000000D).ToString("0.## Cr");
                double value = num / 10000000D;
                if (value > 2 && value < 5)
                {
                    count = 1;
                }
                if (value >= 5 && value < 10)
                {
                    count = 2;
                }
                if (value >= 10 && value < 15)
                {
                    count = 3;
                }
                if (value >= 15 && value < 25)
                {
                    count = 4;
                }
                if (value >= 25)
                {
                    count = 5;
                }

            }
            else if (num >= 100000)
            {
                val = (num / 100000D).ToString("0.## Lacs");
            }

            //if (num >= 100000000)
            //{
            //    return (num / 10000000D).ToString("0.#Crr");
            //}
            //if (num >= 1000000)
            //{
            //    return (num / 10000000D).ToString("0.##Cr");
            //}
            //if (num >= 100000)
            //{
            //    return (num / 1000D).ToString("0.#k");
            //}
            //if (num >= 10000)
            //{
            //    return (num / 1000D).ToString("0.##k");
            //}

            return val;
        }
        public void updatetrackerstock(int id, string Notes)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_updatetracker", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@Notes", Notes);
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }
        public void GetIndex_useless()
        {
            try
            {


                var url = $"https://www.nseindia.com/api/live-analysis-variations?index=gainers";
                using var client = new HttpClient();
                client.BaseAddress = new Uri(url);
                // Add an Accept header for JSON format.
                client.DefaultRequestHeaders.Accept.Add(
                   new MediaTypeWithQualityHeaderValue("application/json"));
                // Get data response
                var response = client.GetAsync(url).Result;
                if (response.IsSuccessStatusCode)
                {
                    // Parse the response body
                    //var dataObjects = response.Content
                    //               .ReadAsAsync<IEnumerable<string>>().Result;
                    //foreach (var d in dataObjects)
                    //{
                    //    Console.WriteLine("{0}", d.Name);
                    //}
                }
                else
                {
                    Console.WriteLine("{0} ({1})", (int)response.StatusCode,
                                  response.ReasonPhrase);
                }

            }
            catch (Exception ex)
            {

                throw;
            }
        }
        public List<StockModel> Indices()
        {
            //string strindices = "";

            List<string> ls = new List<string>
            { "%5EINDIAVIX",
               // "%5EBSESN",
                "%5ENSEI",
                "%5ENSEBANK",

            };
            //ls.Add();
            List<StockModel> resultval = new List<StockModel>();
            foreach (var item in ls)
            {
                string symbol;
                var client = new RestClient(String.Format(apiUrl, item));
                var request = new RestRequest();
                var response = client.Execute(request);
                var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                YahooDataModel.Root data = new YahooDataModel.Root();
                // StockModel data2 = new StockModel();


                try
                {
                    data = JsonSerializer.Deserialize<YahooDataModel.Root>(response.Content, options);

                    var dataval = data.chart.result[0].meta;
                    var prev_close = dataval.chartPreviousClose;
                    // var v52wk = "";
                    //if (item != "%5EINDIAVIX")
                    //{
                    //    v52wk = "<br/><span style = 'color:green;font-size: 20px;' > Life time High: <span style = 'color:darkred;font-size: 20px;' > " + Math.Round(dataval.fiftyTwoWeekHigh, 2) + " </span></span>";
                    //}
                    resultval.Add(new StockModel
                    {

                        change = Math.Round(dataval.regularMarketPrice - prev_close, 2),
                        changepercent = Math.Round(((dataval.regularMarketPrice - prev_close) / prev_close) * 100, 2),
                        companyName = dataval.shortName.Replace("INDIA",""),
                        close = (float)Math.Round(dataval.regularMarketPrice, 2),
                        adjustedClose = (float)Math.Round(dataval.regularMarketPrice, 2),
                        index_html = "<span style = 'color:green;font-size: 20px;' ><span style = 'color:darkred;font-size: 20px;' > " + Math.Round(dataval.regularMarketDayLow, 2) + " </ span ><meter style = 'height:23px;width:170px;margin-left:5px;' min = " + dataval.regularMarketDayLow + " value = " + dataval.regularMarketPrice + " max = " + dataval.regularMarketDayHigh + " ></meter> <span style = 'color:darkred;font-size: 20px;' > " + Math.Round(dataval.regularMarketDayHigh, 2) + " </span></span>"


                    });
                }
                catch (Exception ex)
                {
                }
            }
            return resultval;
        }
        public async Task<List<StockModel>> Indices_()
        {
            //string strindices = "";

            List<string> ls = new List<string>
            { "%5EINDIAVIX",
                // "%5EBSESN",
                "%5ENSEI",
                "%5ENSEBANK",
            };
            //ls.Add();
            List<StockModel> resultval = new List<StockModel>();
            var securities = await Yahoo.Symbols("%5EINDIAVIX",
            "%5ENSEI",
            "%5ENSEBANK").Fields(Field.Symbol, Field.RegularMarketPrice,
            Field.RegularMarketChange, Field.RegularMarketChangePercent, Field.RegularMarketOpen, Field.DividendDate, Field.FiftyTwoWeekLow,
            Field.FiftyTwoWeekHigh, Field.LongName, Field.RegularMarketVolume, Field.RegularMarketDayHigh, Field.RegularMarketDayLow).QueryAsync();
            foreach (var item in ls)
            {
                var aapl = securities[item];
                if (aapl != null)
                {
                    resultval.Add(new StockModel
                    {
                        change = Math.Round(aapl[Field.RegularMarketChange], 2),
                        changepercent = aapl[Field.RegularMarketChangePercent],
                        companyName = aapl[Field.LongName],
                        close = (float)aapl[Field.RegularMarketPrice],
                        adjustedClose = (float)aapl[Field.RegularMarketPrice],
                        high = (float)aapl[Field.RegularMarketDayHigh],
                        low = (float)aapl[Field.RegularMarketDayLow],
                        open = (float)aapl[Field.RegularMarketOpen],
                        index_html = "<meter style='height:35px;margin-left:5px;' min=" + aapl[Field.RegularMarketDayLow] + " value=" + aapl[Field.RegularMarketPrice] + " max=" + aapl[Field.RegularMarketDayHigh] + "></meter>"
                    });
                }
            }
            return resultval;
        }
        public String MoodIndex()
        {
            string strmood = "";
            try
            {
                var doc2 = new HtmlWeb().Load("https://www.tickertape.in/market-mood-index");
                var data2 = doc2.DocumentNode.SelectNodes("//*[@id=\"app-container\"]/div/div[1]/div[1]/div/div[2]/span");
                if (data2 != null)
                {
                    strmood = data2[0].InnerHtml;
                }
            }
            catch (Exception ex)
            {
            }

            return strmood;
        }
        public String GetVIX(out string vix_val, out string indices_text)
        {
            string strVIX = "";
            indices_text = "";
            vix_val = "1";
            try
            {
                string url = "https://www.google.com/finance/quote/INDIA_VIX:INDEXNSE";
                //  string url = "https://www.google.com/finance/quote/INDIA_VIX:INDEXNSE?sa=X&ved=2ahUKEwiBheWv9L6GAxUv-TgGHY_vHIAQ3ecFegQIIxAf";

                var doc2 = new HtmlWeb().Load(url);
                var data2 = doc2.DocumentNode.SelectNodes("//*[@id=\"yDmH0d\"]/c-wiz[2]/div/div[4]/div/main/div[2]/div[1]/div[1]/c-wiz/div/div[1]/div/div[1]/div/div[1]/div/span/div/div\r\n\r\n");
                //   var data3 = doc2.DocumentNode.SelectNodes("//*[@id=\"yDmH0d\"]/c-wiz[2]/div/div[4]/div/main/div[2]/div[1]/div[1]/c-wiz/div/div[1]/div/div[1]/div/div[2]/div/span[1]");

                //  var data3 = doc2.DocumentNode.SelectNodes("//*[@class=\"vReuC GEykwb\"]"); //vReuC GEykwb
                // indices_text = "";// getindicesdata(data3);

                //indices_text = data3[0].OuterHtml+ data3[1].OuterHtml;

                var data4 = doc2.DocumentNode.SelectNodes("//*[@id=\"yDmH0d\"]/c-wiz[2]/div/div[4]/div/main/div[2]/div[1]/div[1]/c-wiz/div/div[1]/div/div[1]"); //vReuC GEykwb
                strVIX = data4[0].OuterHtml;

                var gbr = doc2.DocumentNode.SelectNodes("//*[@class=\"JwB6zf\"]")[1].InnerText;
                //var data32 = doc2.DocumentNode.SelectNodes("//*[@class=\"P2Luy Ebnabc ZYVHBb\"]"); //vReuC GEykwb
                //string s= data32[0].InnerHtml


                if (data2 != null)
                {
                    strVIX = data2[0].InnerHtml + " (" + gbr + ")";
                }
                if (gbr.StartsWith("-"))
                {
                    vix_val = "-1";
                }

            }
            catch (Exception ex)
            {

            }

            return strVIX;
        }
        public string MoodDesc(int val, out string MHeading)
        {
            MHeading = "";
            string returnval = "";
            if (val <= 30)
            {
                MHeading = "Extreme Fear (<30)";
                returnval = "High extreme fear (<20) suggests a good time to open fresh positions, as markets are likely to be oversold and might turn upwards";
            }
            if (val > 30 && val <= 50)
            {
                MHeading = "Fear (30—50)";
                returnval = "It suggests that investors are fearful in the market, but the action to be taken depends on the MMI trajectory.";
            }

            if (val > 50 && val <= 70)
            {
                MHeading = "Greed (50—70)";
                returnval = "High extreme fear (<20) suggests a good time to open fresh positions, as markets are likely to be oversold and might turn upwards";
            }
            if (val > 70)
            {
                MHeading = "Extreme Greed (>70)";
                returnval = "High extreme fear (<20) suggests a good time to open fresh positions, as markets are likely to be oversold and might turn upwards";
            }

            return returnval;

        }
        public void GetResultDatesAsync_notinuse()
        {
            DataSet ds = new DataSet();
            ds.Tables.Add("Records");
            ds.Tables[0].Columns.Add("Script");
            ds.Tables[0].Columns.Add("Date_val");
            ds.Tables[0].Columns.Add("Notes");
            ds.Tables[0].AcceptChanges();

            //  for (int i = 1; i <= 1; i++)
            {
                var start_date = DateTime.Now.Date.AddDays(11).ToString("yyyy-MM-dd");
                var end_date = DateTime.Now.Date.AddDays(12).ToString("yyyy-MM-dd");

                //start_date = "2024-08-19";
                //end_date = "2024-08-20";

                var url = "https://trendlyne.com/equity/calendar/upcoming-results/?start_date=" + start_date + "&end_date=" + end_date + "&corporate_actions=All&defaultStockgroup=all";

                var doc2 = new HtmlWeb().Load(url);
                var data2 = doc2.DocumentNode.ChildNodes[3].ChildNodes[3].SelectSingleNode("//table/tbody");
                //*[@id="TLReactTableV7"]/div[2]/div[3]/div[1]/table
                var val = new StringBuilder();
                foreach (var item in data2.ChildNodes)
                {
                    if (item.ChildNodes.Count > 0)
                    {
                        var comp = item.ChildNodes[1].InnerText.Replace("\n", "").Trim();

                        var script = item.ChildNodes[1].ChildNodes[1].Attributes[0].Value.Split("/")[3];
                        if (string.IsNullOrEmpty(comp))
                        {
                            continue;
                        }
                        var date = item.ChildNodes[3].InnerText.Replace("\n", "").Trim();
                        var notes = item.ChildNodes[5].InnerText.Trim().Replace("\n", "").Replace(",", " ").Replace("&amp;", " and ").Trim();
                        DataRow dr = ds.Tables[0].NewRow();
                        dr[0] = script.ToUpper() + ".NS";
                        dr[1] = date;
                        dr[2] = notes;
                        ds.Tables[0].Rows.Add(dr);
                        ds.Tables[0].AcceptChanges();
                    }

                }
            }
            updateResultDb(ds);
        }
        public void GetResultDatesAsync(string path)
        {

            // load the stock list
            DataSet dsStock = new DataSet();
            dsStock.ReadXml(path + @"\images" + "/stocks.xml", XmlReadMode.InferSchema);
            //end load
            DataSet ds = new DataSet();

            ds.Tables.Add("Records");
            ds.Tables[0].Columns.Add("Script");
            ds.Tables[0].Columns.Add("Date_val");
            ds.Tables[0].Columns.Add("Notes");
            ds.Tables[0].AcceptChanges();

            var start_date = DateTime.Now.Date.AddDays(12).ToString("yyyy-MM-dd");


            var url = $"https://etfeedscache.indiatimes.com/ETService/getMarketCalendarByDate?pageno=1&pagesize=20&startdate=" + start_date + "&callback=processEvent";


            using var client = new HttpClient();
            client.BaseAddress = new Uri(url);
            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(
               new MediaTypeWithQualityHeaderValue("application/json"));
            // Get data response
            var response = client.GetAsync(url).Result;
            var result = new resultmodel.Root();
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                // Parse the response body
                var dataObjects = response.Content.ReadAsStringAsync().Result;
                // JsonConvert.DeserializeObject<resultmodel.Root>(dataObjects);
                dataObjects = dataObjects.Replace("processEvent(", "");
                dataObjects = dataObjects.Remove(dataObjects.Length - 1); ;
                result = JsonSerializer.Deserialize<resultmodel.Root>(dataObjects, options);
                //foreach (var d in dataObjects)
                //{
                //    Console.WriteLine("{0}", d.Name);
                //}
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode,
                              response.ReasonPhrase);
            }

            foreach (var item in result.searchResult)
            {
                var keywords = item.seoName.Replace("-ltd", "").Split('-');
                var s_name = item.name.ToLower().Replace("ltd.", "").Trim();
                //1 with name
                var code = "";

                var rows = dsStock.Tables[0].Select(" COMPANY Like '%" + s_name.Trim().Replace("'", "") + "%'");


                if (rows.Length > 0)
                {
                    code = rows[0][0].ToString() + ".NS";
                }

                if (code != null)
                {
                    DataRow dr = ds.Tables[0].NewRow();
                    dr[0] = code;
                    dr[1] = start_date;
                    dr[2] = item.@event;
                    ds.Tables[0].Rows.Add(dr);
                    ds.Tables[0].AcceptChanges();
                }

            }
            updateResultDb(ds);
        }
        public string updateFIIData(DataSet ds)
        {
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("InsertFIIData", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Employee_Details", ds.Tables[0]);
            cmd.Connection = con;
            con.Open();
            var holiday = cmd.ExecuteScalar();
            con.Close();
            if (holiday == null) { holiday = "N/A"; }
            return holiday.ToString();
        }
        public void GetSharedetailsWeelyAsync()
        {
            DataSet ds = GetAllScripts("A");


            DataSet dsresult = new DataSet();
            dsresult.Tables.Add("Records");
            dsresult.Tables[0].Columns.Add("Script");
            dsresult.Tables[0].Columns.Add("PE");
            dsresult.Tables[0].Columns.Add("FV");
            dsresult.Tables[0].Columns.Add("ROE");
            dsresult.Tables[0].Columns.Add("SECTOR");

            int i = 0;
            ds.Tables[0].AcceptChanges();
            foreach (DataRow item in ds.Tables[0].Rows)
            {
            ineligible:
                i++;
                var url = "https://www.screener.in/company/" + item["Script_code"].ToString().TrimEnd().Replace(".NS", "") + "/consolidated/";
                //url = "https://www.screener.in/company/SBIN/consolidated/";

                var doc2 = new HtmlWeb().Load(url);
                var PE = "";
                var FV = "";
                var ROE = "";
                var SECTOR = "";

                Thread.Sleep(1000);                           //*[@id="top-ratios"]/li[4]/span[2]/span
                var data2 = doc2.DocumentNode.SelectSingleNode("//*[@id=\"top-ratios\"]/li[4]/span[2]/span");
                if (data2 != null)
                {
                    PE = data2.InnerHtml;
                }
                data2 = doc2.DocumentNode.SelectSingleNode("//*[@id=\"top-ratios\"]/li[9]/span[2]/span");
                if (data2 != null)
                {
                    FV = data2.InnerHtml;
                }
                data2 = doc2.DocumentNode.SelectSingleNode("//*[@id=\"top-ratios\"]/li[8]/span[2]/span");
                if (data2 != null)
                {
                    ROE = data2.InnerHtml;
                }
                data2 = doc2.DocumentNode.SelectSingleNode(" //*[@id=\"peers\"]/div[1]/div[1]/p[1]/a[1]");
                if (data2 != null)
                {
                    SECTOR = data2.InnerHtml;
                }



                DataRow dr = dsresult.Tables[0].NewRow();
                dr[0] = item["Script_code"].ToString();
                dr[1] = PE;
                dr[2] = FV;
                dr[3] = ROE;
                dr[4] = SECTOR;

                if (PE == "" && FV == "" && ROE == "" && i < 6)
                {
                    goto ineligible;

                }
                dsresult.Tables[0].Rows.Add(dr);
                dsresult.Tables[0].AcceptChanges();
                i = 0;
                //  Thread.Sleep(5000);
            }
            updateShareDetailsDb(dsresult);
            //    updateResultDb(ds);
            //}
        }
        public void updateResultDb(DataSet ds)
        {
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("InsertResults", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Employee_Details", ds.Tables[0]);
            cmd.Connection = con;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }
        //select * from [ResultHistory] order by 1 desc
        public void RemovePin(int id)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_updatePin", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@id", id);

            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }

        public void updateview(int id, string tracking)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_updateview", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@view", tracking);


            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }
        public List<Nifty50> GetNifty50()
        {
            List<Nifty50> lststockModel = new List<Nifty50>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("select * from Nifty50 order by weight desc", con);
            cmd.CommandType = CommandType.Text;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            DataSet myrec = new DataSet();

            try
            {
                da.Fill(myrec);

                foreach (DataRow dr in myrec.Tables[0].Rows)
                {
                    lststockModel.Add(new Nifty50
                    {
                        Company_Name = Convert.ToString(dr["Company_Name"]),
                        Industry = Convert.ToString(dr["Industry"]),
                        Weight = Convert.ToString(dr["Weight"]),
                        stockModel = GetStockLive(Convert.ToString(dr["symbol"])).Result,
                    });
                }
            }
            finally
            {
                con.Close();
            }
            return lststockModel;
        }
        public int SellStock(string id, string sell_price)
        {
            int i;
            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                con.Open();
                SqlCommand com = new SqlCommand("sp_sellStocks", con);
                com.CommandType = CommandType.StoredProcedure;
                com.Parameters.AddWithValue("@id", id);
                com.Parameters.AddWithValue("@sell_price", sell_price);
                i = com.ExecuteNonQuery();
            }
            return i;
        }
        public void updateShareDetailsDb(DataSet ds)
        {
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("UpdateStockdetails", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Share_Details", ds.Tables[0]);
            cmd.Connection = con;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }
        public DataSet GetAllScripts(string type)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("st_Allscript", con);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            DataSet myrec = new DataSet();
            da.Fill(myrec);
            con.Close();
            return myrec;
        }
        public string GetFIIDATA_Async(out StringBuilder buy, out StringBuilder sold)
        {
            DataSet ds = new DataSet();
            var dsAllscript = GetAllScripts("A");
            ds.Tables.Add("Records");
            ds.Tables[0].Columns.Add("Script");
            ds.Tables[0].Columns.Add("Date_val");
            ds.Tables[0].Columns.Add("Notes");
            ds.Tables[0].AcceptChanges();
            string latest_date = "";
            //  for (int i = 1; i <= 1; i++)
            {
                var url = "https://munafasutra.com/nse/FIIDII/";
                var doc2 = new HtmlWeb().Load(url);
                var data2 = doc2.DocumentNode.SelectSingleNode("//*[@id=\"allFIIDII\"]");
                var data3 = doc2.DocumentNode.SelectSingleNode("//*[@id=\"ActivityFIIDII\"]");
                if (data3 != null)
                {
                    //  doc2.DocumentNode.SelectSingleNode("//*[@id=\"ActivityFIIDII\"]").SelectSingleNode("a").Remove();

                }
                buy = new StringBuilder();
                sold = new StringBuilder();
                var sunmmary = new StringBuilder();
                if (data3 != null)
                {
                    int cnt = 0;
                    foreach (var item in data3.ChildNodes[4].ChildNodes)
                    {
                        var tr = item.InnerHtml;
                        if (item.ChildNodes.Count == 0)
                        {
                            continue;
                        }
                        var color = "green";
                        var val = item.ChildNodes[9].InnerHtml;
                        if (val.Contains('-'))
                        {
                            color = "red";
                        }


                        sunmmary.Append("<tr style='color:" + color + "'>" + item.InnerHtml + "</tr>");
                        if (cnt == 1)
                        {
                            string[] arr = item.InnerHtml.Replace("<td>", "").Split("</td>");
                            latest_date = arr[1].Replace("\r\n", "");

                        }
                        cnt++;
                    }
                }
                if (data2 != null)
                {
                    foreach (var item in data2.ChildNodes[2].ChildNodes)
                    {
                        var tr = item.InnerHtml;
                        if (item.ChildNodes.Count == 0)
                        {
                            continue;
                        }
                        var bgcolor = "transparent";
                        var type = "Buy";
                        var script_code = gettxtbettwen(tr, "(", ")");
                        var drresult = dsAllscript.Tables[0].Select("Script_code='" + script_code + ".NS'");
                        if (drresult != null && drresult.Length > 0)
                        {
                            bgcolor = "yellow";
                        }
                        var buyorsold = item.ChildNodes[3];
                        if (buyorsold.InnerHtml.Trim() == "Sold")
                        {
                            sold.Append("<tr  style='color:red;background:" + bgcolor + "' >" + tr + "</tr>");
                            type = "Sell";
                        }
                        else
                        {
                            buy.Append("<tr style='color:green;background:" + bgcolor + "'>" + tr + "</tr>");
                        }


                        if (drresult != null && drresult.Length > 0)
                        {
                            var notes_val = "";
                            var arrdata = tr.Split("</td>");
                            if (arrdata.Length > 5)
                            {
                                notes_val = arrdata[3].Replace(@"<td>", "").Replace(Environment.NewLine, "");
                                //notes_val = arrdata[3].Replace(@"\r\n<td>", "");

                            }
                            DataRow dr = ds.Tables[0].NewRow();
                            dr["Script"] = script_code;
                            dr["Notes"] = type + "-" + notes_val;
                            dr["Date_val"] = latest_date;
                            ds.Tables[0].Rows.Add(dr);
                        }
                    }
                }
                var holiday = updateFIIData(ds);
                return "<h4>Next Holiday on:" + holiday + "<h4/><br/><br/>" + sunmmary.ToString();
            }

        }
        private string gettxtbettwen(string txt, string first, string last)
        {
            try
            {

                StringBuilder sb = new StringBuilder(txt);
                int pos1 = txt.IndexOf(first) + first.Length;
                int len = (txt.Length) - pos1;
                string reminder = txt.Substring(pos1, len);
                int pos2 = reminder.IndexOf(last) - last.Length + 1;


                return reminder.Substring(0, pos2);
            }
            catch (Exception)
            {

                return "";
            }


        }
        public void InsertDailyPL(decimal todayPL, decimal totalPL)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("DailyPL_Insert", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@dailyPL", todayPL);
            cmd.Parameters.AddWithValue("@TotalPL", totalPL);
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }
        // Today's realized P/L computed straight from the shares table:
        //   SUM( (sell_price - inv_Price) * shares )  for rows sold today.
        // No Yahoo lookups. Rows still open (sold = 0) are ignored because they
        // have no sell_price yet — nothing was "realized" for them today.
        public decimal GetTodayPL()
        {
            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                using (SqlCommand cmd = new SqlCommand(
                    @"SELECT ISNULL(SUM((sell_price - inv_Price) * shares), 0)
                      FROM   dbo.shares
                      WHERE  ISNULL(sold, 0) = 1
                        AND  CAST(sell_date AS DATE) = CAST(GETDATE() AS DATE)", con))
                {
                    con.Open();
                    var val = cmd.ExecuteScalar();
                    if (val == null || val == DBNull.Value) return 0m;
                    return Convert.ToDecimal(val);
                }
            }
        }

        public string brokeragereport()
        {

            var url = "https://trendlyne.com/research-reports/broker/Motilal%20Oswal/";
            var doc2 = new HtmlWeb().Load(url);
            var data2 = doc2.DocumentNode.SelectSingleNode("//*[@id=\"brokerTable\"]");
            var report = new StringBuilder();
            if (data2 != null)
            {
                report.Append(data2.OuterHtml);
                //  doc2.DocumentNode.SelectSingleNode("//*[@id=\"ActivityFIIDII\"]").SelectSingleNode("a").Remove();

            }
            return report.ToString();

        }

        public DataSet GetPortfolioCart(string year)
        {
          
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("GetDailychartData", con);
            cmd.Parameters.AddWithValue("@year", year);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            DataSet myrec = new DataSet();
            da.Fill(myrec);
            con.Close();
            return myrec;
        }


        public DataSet GetPortfolioStockReport(string script_code)
        {

            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("dbo.sp_getPortfolioStocks", con);
            cmd.Parameters.AddWithValue("@scriptcode", script_code);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            DataSet myrec = new DataSet();
            da.Fill(myrec);
            con.Close();
            return myrec;
        }

        public DataSet GetDividends(string script_code)
        {

            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("dbo.sp_getPortfolioStocksDividends", con);
            cmd.Parameters.AddWithValue("@scriptcode", script_code);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            DataSet myrec = new DataSet();
            da.Fill(myrec);
            con.Close();
            return myrec;
        }

        public void AddDividend(string script_code, string date, decimal amount)
        {
            List<ProfitStockList> lststockModel = new List<ProfitStockList>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("insertDividend", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@script", script_code);
            cmd.Parameters.AddWithValue("@date_val", date);
            cmd.Parameters.AddWithValue("@amount", amount);
            SqlDataAdapter da = new SqlDataAdapter();
            da.SelectCommand = cmd;
            con.Open();
            cmd.ExecuteNonQuery();
            con.Close();
        }

        public List<OrderViewModel> GetTodayOrders()
        {
            var orders = new List<OrderViewModel>();
            SqlConnection con = new SqlConnection(ConnectioString);
            SqlCommand cmd = new SqlCommand("sp_getTodaysOrder", con);
            //cmd.CommandType = CommandType.StoredProcedure;
            //cmd.Parameters.AddWithValue("@script", script_code);
            //cmd.Parameters.AddWithValue("@date_val", date);
            //cmd.Parameters.AddWithValue("@amount", amount);
            con.Open();
            SqlDataReader reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                orders.Add(new OrderViewModel
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Symbol = reader["Symbol"].ToString(),
                    TransactionType = reader["TransactionType"].ToString(),
                    Quantity = Convert.ToInt32(reader["Quantity"]),
                    Price = Convert.ToDecimal(reader["Price"]),
                    AveragePrice = Convert.ToDecimal(reader["AveragePrice"]),
                    Status = reader["Status"].ToString(),
                    Exchange = reader["Exchange"].ToString(),
                    OrderTime = reader["OrderTime"] == DBNull.Value
                                ? (DateTime?)null
                                : Convert.ToDateTime(reader["OrderTime"])
                });
            }
            con.Close();

            return orders;
        }

        public List<TradeReportModel> GetTradeReport()
        {
            var list = new List<TradeReportModel>();

            using (SqlConnection con = new SqlConnection(ConnectioString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_getOrdersReport", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    con.Open();
                    SqlDataReader dr = cmd.ExecuteReader();

                    while (dr.Read())
                    {
                        list.Add(new TradeReportModel
                        {
                            Symbol = dr["Symbol"].ToString(),
                            BuyPrice = dr["BuyPrice"] != DBNull.Value ? Convert.ToDecimal(dr["BuyPrice"]) : (decimal?)null,
                            SellPrice = dr["SellPrice"] != DBNull.Value ? Convert.ToDecimal(dr["SellPrice"]) : (decimal?)null,
                            Quantity = Convert.ToInt32(dr["Quantity"]),
                            ProfitLossValue = dr["ProfitLossValue"] != DBNull.Value    ? Convert.ToDecimal(dr["ProfitLossValue"]) : (decimal?)null,
                            ProfitLoss = dr["ProfitLoss"]?.ToString(),
                            Status = dr["Status"].ToString(),
                            Type = dr["Type"].ToString(),
                            BuyOrderTime = dr["BuyOrderTime"].ToString(),
                            SellOrderTime = dr["SellOrderTime"]?.ToString()
                        });
                    }
                }
            }

            return list;
        }
    }
}
