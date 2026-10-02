using PhasmaStrap.Utility.Backend;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Groups FastFlags for the flag editor's card view and gives each a readable title.
    /// Order of sources: the server catalog (when it answered), the app's own preset map, then words in the flag name.
    /// </summary>
    public static class FlagCategories
    {
        public const string FrameRate = "Frame rate";
        public const string Rendering = "Rendering";
        public const string Network = "Network and loading";
        public const string Interface = "Interface";
        public const string Sound = "Sound and voice";
        public const string Telemetry = "Telemetry";
        public const string Debug = "Debug";
        public const string Other = "Other";

        private static readonly string[] DefaultOrder = { FrameRate, Rendering, Network, Interface, Sound, Telemetry, Debug, Other };

        private static readonly Dictionary<string, string> PresetCategory = BuildPresetMap();

        private static readonly string[] TypePrefixes =
        {
            "DFFlag", "DFInt", "DFString", "DFLog", "SFFlag", "SFInt", "SFString", "FFlag", "FInt", "FString", "FLog",
        };

        private static Dictionary<string, string> BuildPresetMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, flag) in FastFlagManager.PresetFlags)
            {
                string group = key.Split('.')[0];

                string category = group switch
                {
                    "Telemetry" => Telemetry,
                    "Debug" => Debug,
                    "Network" or "Recommended" or "Preload" or "Cache" or "Memory" => Network,
                    "UI" or "Menu" or "Camera" or "DarkMode" or "Fake" => Interface,
                    _ => Rendering,
                };

                if (category == Rendering && IsFrameRateWord(key + flag))
                    category = FrameRate;

                map.TryAdd(flag, category);
            }

            return map;
        }

        private static bool IsFrameRateWord(string text) =>
            Contains(text, "Fps") || Contains(text, "Framerate") || Contains(text, "FrameRate") || Contains(text, "RefreshRate")
            || Contains(text, "FrameTime") || Contains(text, "TaskScheduler");

        private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

        private static bool ContainsAny(string text, params string[] words) => words.Any(w => Contains(text, w));

        public static string CategoryOf(string flagName)
        {
            FlagCatalogEntry? known = FlagsCaptureApi.Describe(flagName);
            if (known is not null && known.Category.Length > 0)
                return known.Category;

            if (PresetCategory.TryGetValue(flagName, out string? preset))
                return preset;

            string name = StripType(flagName);

            if (ContainsAny(name, "Telemetry", "Analytics"))
                return Telemetry;

            if (IsFrameRateWord(name) || ContainsAny(name, "FRM"))
                return FrameRate;

            if (ContainsAny(name, "Voice", "Audio", "Sound", "Microphone"))
                return Sound;

            if (ContainsAny(name, "Network", "Packet", "Payload", "Bandwidth", "MTU", "RakNet", "Replicat", "Ping", "Http", "Cache", "Preload", "SignalR", "Teleport"))
                return Network;

            if (ContainsAny(name, "Render", "Graphic", "Texture", "Light", "Shadow", "Shader", "Mesh", "Terrain", "D3D", "Vulkan", "OpenGL", "MSAA", "PostFx", "Sky", "Grass", "Particle", "LevelOfDetail", "Voxel", "Bloom", "Fog"))
                return Rendering;

            if (ContainsAny(name, "Menu", "Chat", "Gui", "Font", "Text", "Unibar", "Chrome", "Camera", "Haptic", "Fullscreen", "Theme", "Icon"))
                return Interface;

            if (name.StartsWith("Debug", StringComparison.OrdinalIgnoreCase))
                return Debug;

            return Other;
        }

        /// <summary>Position of a category in the card list; unknown categories go before Other, in name order.</summary>
        public static int OrderOf(string category)
        {
            List<string>? serverOrder = FlagsCaptureApi.Catalog?.Categories;
            if (serverOrder is { Count: > 0 })
            {
                int index = serverOrder.FindIndex(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    return index;
            }

            int local = Array.IndexOf(DefaultOrder, category);
            return local >= 0 ? 100 + local : 100 + DefaultOrder.Length - 1;
        }

        /// <summary>Server title when there is one, otherwise the flag name in words ("Task scheduler target fps").</summary>
        public static string TitleOf(string flagName)
        {
            FlagCatalogEntry? known = FlagsCaptureApi.Describe(flagName);
            if (known is not null && known.Title.Length > 0)
                return known.Title;

            string name = StripType(flagName);
            if (name.Length == 0)
                return flagName;

            var words = new List<string>();
            var current = new StringBuilder();

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool boundary = current.Length > 0 && (
                    (char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                    || (char.IsDigit(c) != char.IsDigit(name[i - 1]))
                    || c == '_');

                if (boundary)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                if (c != '_')
                    current.Append(c);
            }

            if (current.Length > 0)
                words.Add(current.ToString());

            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                bool acronym = word.Length > 1 && word.All(ch => char.IsUpper(ch) || char.IsDigit(ch));
                if (!acronym)
                    words[i] = i == 0 ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant() : word.ToLowerInvariant();
            }

            return string.Join(" ", words);
        }

        /// <summary>Server description, or "" when there is none.</summary>
        public static string DescriptionOf(string flagName) => FlagsCaptureApi.Describe(flagName)?.Description ?? "";

        private static string StripType(string flagName)
        {
            foreach (string prefix in TypePrefixes)
            {
                if (flagName.StartsWith(prefix, StringComparison.Ordinal) && flagName.Length > prefix.Length)
                    return flagName[prefix.Length..];
            }

            return flagName;
        }

        /// <summary>True for FFlag style switches whose value is True or False.</summary>
        public static bool IsSwitch(string flagName, string value) =>
            (flagName.StartsWith("FFlag", StringComparison.Ordinal) || flagName.StartsWith("DFFlag", StringComparison.Ordinal) || flagName.StartsWith("SFFlag", StringComparison.Ordinal))
            && (value.Equals("True", StringComparison.OrdinalIgnoreCase) || value.Equals("False", StringComparison.OrdinalIgnoreCase) || value.Length == 0);
    }
}
