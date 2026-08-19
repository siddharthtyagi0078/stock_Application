using System.Net;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;

namespace StockWebApplications.Controllers
{
    public class ReferenceController : Controller
    {
        private static readonly HttpClient http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        static ReferenceController()
        {
            // Indiankanoon serves 403 without a real UA.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/121.0 Safari/537.36");
            http.Timeout = TimeSpan.FromSeconds(30);
        }

        public IActionResult Index() => View();

        // Scrape indiankanoon.org search results (Supreme Court, top 20).
        [HttpGet]
        public async Task<JsonResult> Search(string q)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                    return Json(new { ok = true, results = new object[0] });

                var baseUrl = "https://indiankanoon.org/search/?formInput=" +
                              Uri.EscapeDataString(q + " doctypes:supremecourt");

                // Fetch 3 pages in parallel — one sequential timeout would blow the budget.
                var pages = await Task.WhenAll(
                    Enumerable.Range(0, 3).Select(async p =>
                    {
                        try { return await http.GetStringAsync(baseUrl + "&pagenum=" + p); }
                        catch { return null; }
                    }));

                var results = new List<object>();
                var seen    = new HashSet<string>();
                foreach (var html in pages)
                {
                    if (html == null) continue;
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    var nodes = doc.DocumentNode.SelectNodes(
                        "//*[contains(@class,'result_title')]//a[@href]");
                    if (nodes == null) continue;

                    foreach (var a in nodes)
                    {
                        var href  = a.GetAttributeValue("href", "").Trim();
                        var title = WebUtility.HtmlDecode(a.InnerText).Trim();
                        if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(title)) continue;
                        if (!seen.Add(href)) continue;

                        if (href.StartsWith("/"))
                            href = "https://indiankanoon.org" + href;

                        results.Add(new { title, url = href });
                        if (results.Count >= 25) break;
                    }
                    if (results.Count >= 25) break;
                }
                return Json(new { ok = true, results });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message, results = new object[0] });
            }
        }

        // Fetch a single indiankanoon case page and return the main judgment HTML
        // so we can render it inside our modal (indiankanoon sets X-Frame-Options).
        [HttpGet]
        public async Task<JsonResult> GetCase(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url) ||
                    !url.StartsWith("https://indiankanoon.org/", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { ok = false, error = "Invalid URL" });
                }

                // Search results point at /docfragment/<id>/ (only context snippets).
                // Full judgment lives at /doc/<id>/ — rewrite so we always fetch the whole case.
                var m = System.Text.RegularExpressions.Regex.Match(
                    url, @"^https://indiankanoon\.org/docfragment/(\d+)/",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                    url = "https://indiankanoon.org/doc/" + m.Groups[1].Value + "/";

                var html = await http.GetStringAsync(url);
                var doc  = new HtmlDocument();
                doc.LoadHtml(html);

                // The judgment (title, citations, author, bench, body) lives in
                // <div class="judgments"> — this excludes the PRISMAI ad sidebar,
                // nav, and other chrome that main-content also contains.
                var main = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'judgments')]")
                        ?? doc.DocumentNode.SelectSingleNode("//div[@id='doc']")
                        ?? doc.GetElementbyId("main-content")
                        ?? doc.GetElementbyId("pre_1");

                var title = doc.DocumentNode.SelectSingleNode("//h2[@class='doc_title']")?.InnerText?.Trim()
                          ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim()
                          ?? "";

                if (main == null)
                    return Json(new { ok = false, error = "Case body not found on page." });

                // Strip scripts/styles/iframes and indiankanoon's chrome (search box,
                // ads, nav, sidebars) so the modal shows only the judgment.
                var junk = main.SelectNodes(
                    ".//script|.//style|.//iframe|.//link|.//noscript|.//aside|" +
                    ".//form|.//nav|" +
                    ".//*[contains(@class,'sr-only')]|" +
                    ".//*[contains(@class,'ads')]|" +
                    ".//*[contains(@class,'promo')]|" +
                    ".//*[contains(@class,'left_column')]|" +
                    ".//*[contains(@class,'sticky_column')]|" +
                    ".//*[contains(@class,'result_searchbox')]|" +
                    ".//*[contains(@class,'sidebar')]|" +
                    ".//*[contains(@class,'related')]|" +
                    ".//*[contains(@class,'user_note')]|" +
                    ".//*[contains(@class,'covers')]");
                if (junk != null)
                    foreach (var n in junk) n.Remove();

                return Json(new { ok = true, title, body = main.InnerHtml });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }
    }
}
