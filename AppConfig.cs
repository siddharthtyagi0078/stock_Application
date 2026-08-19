using Microsoft.Extensions.Configuration;

namespace StockWebApplications
{
    // Static accessor for IConfiguration so classes constructed outside DI
    // (e.g. `new DataAccess()`) can still read appsettings.json.
    // Program.cs assigns Configuration during startup.
    public static class AppConfig
    {
        public static IConfiguration Configuration { get; set; }

        public static string GetConnectionString(string name = "DefaultConnection")
            => Configuration?.GetConnectionString(name);
    }
}
