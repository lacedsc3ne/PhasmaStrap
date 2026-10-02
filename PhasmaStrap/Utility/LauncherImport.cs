using System.Text.Json.Nodes;

namespace PhasmaStrap.Utility
{
    public static class LauncherImport
    {
        private const string LOG_IDENT = "LauncherImport";

        private static readonly string[] Known = { "Bloxstrap", "Fishstrap", "Froststrap", "Voidstrap" };

        private static readonly string[] NeverCopied =
        {
            "Locale", "CustomIntegrations", "BootstrapperIconCustomLocation", "RobloxIconCustomLocation",
            "SelectedCustomTheme", "BackgroundImagePath",
        };

        public sealed class Found
        {
            public string Name { get; init; } = "";
            public string Folder { get; init; } = "";
            public bool HasSettings { get; init; }
            public int Flags { get; init; }

            public string Summary => $"{Name}: {(HasSettings ? "settings" : "no settings")}, {Flags} FastFlag{(Flags == 1 ? "" : "s")}";
        }

        public sealed class Result
        {
            public int Settings { get; init; }
            public int Flags { get; init; }
        }

        private static string SettingsOf(string folder) => Path.Combine(folder, "Settings.json");

        private static string FlagsOf(string folder) => Path.Combine(folder, "Modifications", "ClientSettings", "ClientAppSettings.json");

        public static List<Found> Look()
        {
            var found = new List<Found>();

            foreach (string name in Known)
            {
                string folder = Path.Combine(Paths.LocalAppData, name);

                if (!Directory.Exists(folder) || string.Equals(folder, Paths.Base, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool settings = File.Exists(SettingsOf(folder));
                int flags = ReadObject(FlagsOf(folder))?.Count ?? 0;

                if (settings || flags > 0)
                    found.Add(new Found { Name = name, Folder = folder, HasSettings = settings, Flags = flags });
            }

            return found;
        }

        public static Result Import(Found source)
        {
            int settings = 0;
            int flags = 0;

            if (ReadObject(SettingsOf(source.Folder)) is JsonObject theirs)
            {
                JsonObject ours = JsonSerializer.SerializeToNode(App.Settings.Prop)!.AsObject();

                foreach ((string key, JsonNode? value) in theirs.ToList())
                {
                    if (value is null || !ours.ContainsKey(key) || NeverCopied.Contains(key))
                        continue;

                    JsonNode? current = ours[key];

                    if (current is null || current.GetType() != value.GetType())
                        continue;

                    if (value is JsonValue && current is JsonValue && Kind(value) != Kind(current))
                        continue;

                    if (current.ToJsonString() == value.ToJsonString())
                        continue;

                    ours[key] = JsonNode.Parse(value.ToJsonString());
                    settings++;
                }

                if (settings > 0)
                {
                    try
                    {
                        if (ours.Deserialize<Models.Persistable.Settings>() is Models.Persistable.Settings merged)
                            App.Settings.Prop = merged;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"{source.Name}'s settings did not fit: {ex.Message}");
                        settings = 0;
                    }
                }
            }

            if (ReadObject(FlagsOf(source.Folder)) is JsonObject theirFlags)
            {
                foreach ((string name, JsonNode? value) in theirFlags)
                {
                    if (value is null)
                        continue;

                    string text = value is JsonValue single && single.TryGetValue(out string? raw) ? raw : value.ToJsonString();

                    if (App.FastFlags.GetValue(name) == text)
                        continue;

                    App.FastFlags.SetValue(name, text);
                    flags++;
                }
            }

            App.Logger.WriteLine(LOG_IDENT, $"Took {settings} setting(s) and {flags} FastFlag(s) from {source.Name}");

            return new Result { Settings = settings, Flags = flags };
        }

        private static char Kind(JsonNode node)
        {
            string json = node.ToJsonString();

            if (json.Length == 0)
                return '?';

            return json[0] switch
            {
                '"' => 's',
                't' or 'f' => 'b',
                _ => 'n',
            };
        }

        private static JsonObject? ReadObject(string file)
        {
            try
            {
                return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read {file}: {ex.Message}");
                return null;
            }
        }
    }
}
