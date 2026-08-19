using System.Text;
using Newtonsoft.Json.Linq;

namespace StockWebApplications.Services
{
    // Thin wrapper over Google Gemini generateContent. Key + model come from
    // appsettings.json -> AiSummary (same config the strategy-comment feature uses).
    public class GeminiClient
    {
        private static readonly HttpClient Http = new HttpClient();
        private readonly string _apiKey;
        private readonly string _model;

        public GeminiClient(IConfiguration config)
        {
            _apiKey = config["AiSummary:ApiKey"] ?? "";
            _model  = string.IsNullOrWhiteSpace(config["AiSummary:Model"])
                ? "gemini-3.5-flash"
                : config["AiSummary:Model"];
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        // Pulls "retry in 17.5s" out of a 429 body; falls back if not present.
        private static double ParseRetrySeconds(string body, double fallback)
        {
            var m = System.Text.RegularExpressions.Regex.Match(body ?? "", @"retry in ([\d.]+)s");
            return m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s + 1 : fallback;
        }

        // Returns the model's text, or null on any failure (caller decides fallback).
        public async Task<string?> GenerateAsync(string prompt, int timeoutSeconds = 30)
        {
            if (!IsConfigured) return null;

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var payload = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject { ["parts"] = new JArray { new JObject { ["text"] = prompt } } }
                }
            };

            var bodyJson = payload.ToString();

            // Never throws — returns null on any failure (timeout, reset, 429, quota).
            // Each attempt gets its own timeout so one back-off can't cancel the next.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    // Content must be rebuilt each attempt (a sent StringContent can't be reused).
                    using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                    var resp = await Http.PostAsync(url, content, cts.Token);

                    if ((int)resp.StatusCode == 429)
                    {
                        if (attempt == 3) return null;   // give up: quota exhausted
                        var body429 = await resp.Content.ReadAsStringAsync(cts.Token);
                        var wait = Math.Min(ParseRetrySeconds(body429, fallback: 4 * attempt), 8);
                        await Task.Delay(TimeSpan.FromSeconds(wait));   // not tied to cts
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode) return null;

                    var okBody = await resp.Content.ReadAsStringAsync(cts.Token);
                    var json = JObject.Parse(okBody);
                    return json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>()?.Trim();
                }
                catch (Exception) when (attempt < 3)
                {
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception)
                {
                    return null;   // final attempt failed — surface as "no response"
                }
            }
            return null;
        }
    }
}
