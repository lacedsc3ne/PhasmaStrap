namespace PhasmaStrap.Utility.Backend
{
    /// <summary>Plain language name, one line description and category for one FastFlag. Every field may be empty.</summary>
    public sealed class FlagCatalogEntry
    {
        /// <summary>Exact flag name, for example "DFIntTaskSchedulerTargetFps".</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Short title, for example "Frame rate cap".</summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>One line, for example "Lifts the 60 FPS limit".</summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        /// <summary>One of the category names in <see cref="FlagCatalog.Categories"/>, for example "Rendering".</summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = "";
    }

    public sealed class FlagCatalog
    {
        /// <summary>Category names in the order the flag editor should list them.</summary>
        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("flags")]
        public List<FlagCatalogEntry> Flags { get; set; } = new();
    }

    /// <summary>Server calls for the FastFlags and Capture area. Every call returns null while the server can't answer.</summary>
    public static class FlagsCaptureApi
    {
        private static FlagCatalog? _catalog;
        private static Dictionary<string, FlagCatalogEntry>? _byName;
        private static Task<FlagCatalog?>? _loading;

        /// <summary>The catalog once it has loaded, else null. Call <see cref="LoadFlagCatalogAsync"/> first.</summary>
        public static FlagCatalog? Catalog => _catalog;

        /// <summary>GET /v1/flags/catalog (public). Cached for 12 hours; only one request runs at a time.</summary>
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

        /// <summary>What the server knows about this flag, or null.</summary>
        public static FlagCatalogEntry? Describe(string flagName) =>
            _byName is not null && _byName.TryGetValue(flagName, out FlagCatalogEntry? entry) ? entry : null;
    }
}
