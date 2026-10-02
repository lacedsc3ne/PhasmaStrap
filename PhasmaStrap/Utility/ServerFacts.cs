using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Small shared helpers for showing a public server: region text and badge, continent, ping tone, uptime,
    /// and the regions already known from your own session history.
    /// </summary>
    public static class ServerFacts
    {
        private static readonly Dictionary<string, string> _continents = new(StringComparer.OrdinalIgnoreCase)
        {
            ["US"] = "NA", ["CA"] = "NA", ["MX"] = "NA",
            ["BR"] = "SA", ["CL"] = "SA", ["AR"] = "SA", ["CO"] = "SA", ["PE"] = "SA",
            ["GB"] = "EU", ["UK"] = "EU", ["DE"] = "EU", ["NL"] = "EU", ["FR"] = "EU", ["PL"] = "EU", ["ES"] = "EU", ["IT"] = "EU",
            ["SE"] = "EU", ["NO"] = "EU", ["FI"] = "EU", ["DK"] = "EU", ["IE"] = "EU", ["BE"] = "EU", ["AT"] = "EU", ["CH"] = "EU",
            ["CZ"] = "EU", ["PT"] = "EU", ["RO"] = "EU", ["BG"] = "EU", ["HU"] = "EU", ["GR"] = "EU", ["UA"] = "EU", ["TR"] = "EU", ["RU"] = "EU",
            ["SG"] = "AS", ["JP"] = "AS", ["IN"] = "AS", ["HK"] = "AS", ["KR"] = "AS", ["TW"] = "AS", ["AE"] = "AS", ["ID"] = "AS",
            ["MY"] = "AS", ["TH"] = "AS", ["PH"] = "AS", ["VN"] = "AS", ["SA"] = "AS", ["IL"] = "AS",
            ["AU"] = "OC", ["NZ"] = "OC",
            ["ZA"] = "AF", ["EG"] = "AF", ["NG"] = "AF", ["KE"] = "AF",
        };

        private static readonly Dictionary<string, string> _codes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USA"] = "US", ["United States"] = "US", ["United States of America"] = "US",
            ["UK"] = "GB", ["United Kingdom"] = "GB", ["Great Britain"] = "GB", ["England"] = "GB",
            ["Netherlands"] = "NL", ["Holland"] = "NL", ["France"] = "FR", ["Germany"] = "DE", ["Poland"] = "PL",
            ["India"] = "IN", ["Japan"] = "JP", ["Singapore"] = "SG", ["Australia"] = "AU", ["China"] = "HK", ["Hong Kong"] = "HK",
            ["Canada"] = "CA", ["Brazil"] = "BR", ["South Korea"] = "KR", ["Korea"] = "KR", ["Taiwan"] = "TW",
            ["South Africa"] = "ZA", ["UAE"] = "AE", ["United Arab Emirates"] = "AE", ["Russia"] = "RU",
            ["Mexico"] = "MX", ["Chile"] = "CL", ["Argentina"] = "AR", ["Spain"] = "ES", ["Italy"] = "IT", ["Sweden"] = "SE",
        };

        /// <summary>Two letter country code for a code or a country name, as far as it is known.</summary>
        public static string CountryCode(string? country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return "";

            string trimmed = country.Trim();
            if (_codes.TryGetValue(trimmed, out string? code))
                return code;

            return trimmed.Length == 2 ? trimmed.ToUpperInvariant() : trimmed;
        }

        public static string ContinentName(string continent) => continent.ToUpperInvariant() switch
        {
            "EU" => "Europe",
            "NA" => "North America",
            "SA" => "South America",
            "AS" => "Asia",
            "OC" => "Oceania",
            "AF" => "Africa",
            _ => "",
        };

        /// <summary>Splits "Frankfurt, DE" into its city and two letter country.</summary>
        public static (string City, string Country) SplitRegion(string? region)
        {
            if (string.IsNullOrWhiteSpace(region))
                return ("", "");

            int comma = region.LastIndexOf(", ", StringComparison.Ordinal);
            if (comma <= 0)
                return (region.Trim(), "");

            return (region[..comma].Trim(), CountryCode(region[(comma + 2)..]));
        }

        public static string ContinentOf(string country, string fallback = "")
        {
            if (fallback.Length > 0)
                return fallback.ToUpperInvariant();

            string code = CountryCode(country);
            return code.Length > 0 && _continents.TryGetValue(code, out string? continent) ? continent : "";
        }

        /// <summary>The short tag in front of a region: EU for anywhere in Europe, otherwise the country code.</summary>
        public static string Badge(string country, string continent)
        {
            if (continent.Equals("EU", StringComparison.OrdinalIgnoreCase))
                return "EU";

            string code = CountryCode(country);
            return code.Length == 2 ? code : continent.ToUpperInvariant();
        }

        /// <summary>good, warn, bad or none, matched by the colour triggers on every ping cell.</summary>
        public static string PingTone(int ping) => ping <= 0 ? "none" : ping < 60 ? "good" : ping < 120 ? "warn" : "bad";

        public static string ShortJobId(string jobId) => jobId.Length > 12 ? $"{jobId[..4]}…{jobId[^4..]}" : jobId;

        public static string Uptime(DateTime? startedUtc)
        {
            if (startedUtc is not DateTime started)
                return "";

            TimeSpan up = DateTime.UtcNow - started;
            if (up < TimeSpan.Zero)
                return "";

            if (up.TotalHours >= 24)
                return $"{(int)up.TotalDays}d {up.Hours}h";

            return up.TotalHours >= 1 ? $"{(int)up.TotalHours}h {up.Minutes:00}m" : $"{Math.Max(1, up.Minutes)}m";
        }

        private static Dictionary<string, string>? _historyRegions;
        private static DateTime _historyRead = DateTime.MinValue;
        private static readonly object _lock = new();

        /// <summary>Job id to region for every server you have been in, read from the session history on Activity.</summary>
        public static Dictionary<string, string> HistoryRegions()
        {
            lock (_lock)
            {
                if (_historyRegions is not null && DateTime.UtcNow - _historyRead < TimeSpan.FromMinutes(1))
                    return _historyRegions;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    foreach (ServerVisit visit in SessionStore.Shared.Load().Sessions.SelectMany(s => s.Visits))
                    {
                        if (visit.JobId.Length > 0 && visit.Region.Length > 0)
                            map[visit.JobId] = visit.Region;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("ServerFacts", $"Session history unreadable: {ex.Message}");
                }

                _historyRegions = map;
                _historyRead = DateTime.UtcNow;
                return map;
            }
        }
    }
}
