using System.Text.RegularExpressions;

namespace StockWebApplications.Services
{
    // Validates AI-generated SQL before it is ever executed. The rules are
    // deliberately strict: a single read-only SELECT/CTE, row-capped, with no
    // statement that could mutate data or reach outside the query surface.
    // Execution ALSO runs inside a transaction that is always rolled back
    // (see DataAccess.RunReadOnlyQuery) as a second line of defence.
    public static class SqlGuard
    {
        private static readonly string[] Forbidden =
        {
            "insert", "update", "delete", "drop", "alter", "create", "truncate",
            "exec", "execute", "merge", "grant", "revoke", "backup", "restore",
            "shutdown", "xp_", "sp_", "openrowset", "openquery", "waitfor",
            "into"   // blocks SELECT ... INTO (table creation)
        };

        // Returns true if safe, otherwise sets `error` and returns false.
        // `cleaned` is the normalised SQL to execute (fences stripped, row cap added).
        public static bool TryValidate(string? raw, out string cleaned, out string error)
        {
            cleaned = "";
            error = "";

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "No SQL was produced.";
                return false;
            }

            var sql = raw.Trim();

            // Strip ```sql ... ``` / ``` ... ``` code fences the model may add.
            sql = Regex.Replace(sql, "^```[a-zA-Z]*", "").Trim();
            sql = Regex.Replace(sql, "```$", "").Trim();

            // Remove comments (line and block) so they can't hide keywords.
            sql = Regex.Replace(sql, "--.*?$", "", RegexOptions.Multiline);
            sql = Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline);
            sql = sql.Trim().TrimEnd(';').Trim();

            if (sql.Length == 0)
            {
                error = "Empty query.";
                return false;
            }

            // Single statement only — no embedded semicolons.
            if (sql.Contains(';'))
            {
                error = "Only a single statement is allowed.";
                return false;
            }

            // Must be a read query.
            var lower = sql.ToLowerInvariant();
            if (!(lower.StartsWith("select") || lower.StartsWith("with")))
            {
                error = "Only SELECT queries are allowed.";
                return false;
            }

            // Word-boundary check for dangerous keywords.
            foreach (var kw in Forbidden)
            {
                var pattern = kw.EndsWith("_")
                    ? Regex.Escape(kw)                    // xp_ / sp_ : prefix match
                    : $@"\b{Regex.Escape(kw)}\b";
                if (Regex.IsMatch(lower, pattern))
                {
                    error = $"Query rejected: contains '{kw}'.";
                    return false;
                }
            }

            // Enforce a row cap: inject TOP if absent. In T-SQL, TOP must come
            // AFTER an optional DISTINCT, i.e. "SELECT DISTINCT TOP 500 ...".
            bool hasTop = Regex.IsMatch(lower, @"^\s*select\s+(distinct\s+)?top\b");
            bool isCte  = Regex.IsMatch(lower, @"^\s*with\b");
            if (!hasTop && !isCte)
            {
                sql = Regex.Replace(sql, @"^\s*select\s+(distinct\s+)?",
                    m => "SELECT " + (m.Groups[1].Success ? "DISTINCT " : "") + "TOP 500 ",
                    RegexOptions.IgnoreCase);
            }

            cleaned = sql;
            return true;
        }
    }
}
