# Stock Application — Trading & Options Position Manager (ASP.NET Core / C#)

An **ASP.NET Core MVC** web application (C#) for tracking stock and **multi-leg options positions**, with live market data and integration to the **Angel One** broker API. Built as a public sample of my backend engineering style — most of my production work (banking, enterprise platforms) is under NDA, so this is a representative example.

<!-- Add a screenshot or GIF here — a trading UI is very visual and helps reviewers a lot.
![Demo](docs/demo.png) -->

## What it does
- Manages stock and **multi-leg options** positions, including per-leg expiry tracking and position exit handling.
- Integrates with the **Angel One (SmartAPI)** broker for account/market data.
- Pulls live quote data (Yahoo Finance) for pricing.
- Persists positions and trades in **SQL Server**, with versioned schema migrations.

## Tech stack
| Area | Tech |
|---|---|
| Language | C# |
| Framework | ASP.NET Core MVC (Controllers / Models / Views) |
| Data | SQL Server, ADO.NET data access, SQL migration scripts |
| Integrations | Angel One SmartAPI (broker), Yahoo Finance (quotes) |
| Frontend | Razor views, JavaScript, static assets (`wwwroot`) |

## Architecture
```
StockWebApplications/
├── Controllers/     # MVC controllers (request handling)
├── Models/          # domain & view models
├── Views/           # Razor UI
├── AngelOneClient.cs # broker API client (auth via TOTP, config-driven)
├── DataAccess.cs    # SQL Server data layer
├── Program.cs       # app startup & DI
├── wwwroot/         # static assets
└── *_Migration.sql  # schema migrations (leg IDs, exit position, per-leg expiry)
```

## Configuration
All secrets are read from configuration — **nothing is hardcoded**. Provide your own via `appsettings.json` (gitignored) or user-secrets:

```jsonc
{
  "AngelOne": {
    "ApiKey": "",
    "ClientCode": "",
    "Pin": "",
    "TotpSecret": ""
  },
  "ConnectionStrings": {
    "Default": ""
  }
}
```
> ⚠️ Never commit real credentials. Use `dotnet user-secrets` for local dev.

## Getting started
```bash
dotnet restore
# apply the *_Migration.sql scripts to your SQL Server database
dotnet run --project StockWebApplications.csproj
```

## About the author
Siddharth Tyagi — Principal .NET Engineer & Solution Architect, 20+ years. Remote from India (IST).
[LinkedIn](https://www.linkedin.com/in/siddharthtyagi/) · siddharth.tyagi@hotmail.com

---
### One-line GitHub description (paste into the repo's "About" field)
`ASP.NET Core (C#) app for stock & multi-leg options position tracking, with Angel One broker integration and SQL Server persistence.`

### Topics to add
`csharp` `dotnet` `aspnetcore` `mvc` `sql-server` `options-trading` `stock-market` `angel-one` `trading`
