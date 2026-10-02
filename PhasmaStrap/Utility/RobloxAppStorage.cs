using System.Text.Encodings.Web;
using System.Text.Json.Nodes;

namespace PhasmaStrap.Utility
{
    public static class RobloxAppStorage
    {
        private const string LOG_IDENT = "RobloxAppStorage";

        public const string Leave = "Leave";

        public static readonly string[] BackgroundChoices = { Leave, "On", "Off" };

        public static readonly string[] ThemeChoices = { Leave, "Dark", "Light" };

        private static string FilePath => Path.Combine(Paths.LocalAppData, "Roblox", "LocalStorage", "appStorage.json");

        private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static void Apply()
        {
            string background = App.Settings.Prop.RobloxBackgroundApp;
            string theme = App.Settings.Prop.RobloxAppTheme;

            if (background == Leave && theme == Leave)
                return;

            try
            {
                if (!File.Exists(FilePath))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox has not written its app storage yet, nothing to change");
                    return;
                }

                if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject storage)
                    return;

                var changed = new List<string>();

                if (background != Leave)
                {
                    string state = background == "On" ? "true" : "false";

                    Put(storage, "LaunchAtStartup", state, changed);
                    Put(storage, "MinimizeToTray", state, changed);
                    Put(storage, "SystemTrayModalShown", "true", changed);
                }

                if (theme != Leave)
                {
                    string value = theme.ToLowerInvariant();

                    Put(storage, "AuthenticatedTheme", value, changed);

                    if (storage["UserId"] is JsonValue idNode && idNode.TryGetValue(out string? userId) && !string.IsNullOrEmpty(userId))
                    {
                        JsonObject perUser = storage["DeviceLevelTheme"] is JsonValue mapNode
                            && mapNode.TryGetValue(out string? raw)
                            && !string.IsNullOrWhiteSpace(raw)
                            && JsonNode.Parse(raw) is JsonObject parsed
                                ? parsed
                                : new JsonObject();

                        if (perUser[userId]?.GetValue<string>() != value)
                        {
                            perUser[userId] = value;
                            storage["DeviceLevelTheme"] = perUser.ToJsonString(Options);
                            changed.Add("DeviceLevelTheme");
                        }
                    }
                }

                if (changed.Count == 0)
                    return;

                string temporary = FilePath + ".phasma.tmp";
                File.WriteAllText(temporary, storage.ToJsonString(Options));
                File.Move(temporary, FilePath, true);

                App.Logger.WriteLine(LOG_IDENT, $"Set {string.Join(", ", changed)} (background app {background}, theme {theme})");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not change Roblox's app storage: {ex.Message}");
            }
        }

        private static void Put(JsonObject storage, string key, string value, List<string> changed)
        {
            if (storage[key] is JsonValue existing && existing.TryGetValue(out string? current) && current == value)
                return;

            storage[key] = value;
            changed.Add(key);
        }
    }
}
