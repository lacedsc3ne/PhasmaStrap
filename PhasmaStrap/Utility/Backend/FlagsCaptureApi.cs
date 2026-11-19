namespace PhasmaStrap.Utility.Backend
{
    public sealed class FlagCatalogEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";
    }

    public sealed class FlagCatalog
    {
        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("flags")]
        public List<FlagCatalogEntry> Flags { get; set; } = new();
    }

    public static class FlagsCaptureApi
    {
        private static FlagCatalog? _catalog;
        private static Dictionary<string, FlagCatalogEntry>? _byName;
        private static Task<FlagCatalog?>? _loading;

        public static FlagCatalog? Catalog => _catalog;

        public static Task<FlagCatalog?> LoadFlagCatalogAsync()
        {
            if (_catalog is not null)
                return Task.FromResult<FlagCatalog?>(_catalog);

            return _loading ??= LoadAsync();

            static async Task<FlagCatalog?> LoadAsync()
            {
                FlagCatalog? catalog = await PhasmaApi.GetAsync<FlagCatalog>("/v1/flags/catalog", cacheFor: TimeSpan.FromHours(12));

                if (catalog is not null)
                {
                    _byName = catalog.Flags
                        .Where(f => f.Name.Length > 0)
                        .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                    _catalog = catalog;
                }

                _loading = null;
                return catalog;
            }
        }

        public static FlagCatalogEntry? Describe(string flagName) =>
            _byName is not null && _byName.TryGetValue(flagName, out FlagCatalogEntry? entry) ? entry : null;
    }
}
