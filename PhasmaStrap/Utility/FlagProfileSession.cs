using PhasmaStrap.Models.Entities;
using PhasmaStrap.UI;

namespace PhasmaStrap.Utility
{
    internal static class FlagProfileSession
    {
        private const string LOG_IDENT = "FlagProfileSession";

        public sealed record Wanted(string ProfileName, string Signature)
        {
            public static readonly Wanted None = new("", "");

            public bool IsNone => Signature.Length == 0;

            public static Wanted Of(FlagProfile? profile) =>
                profile is null || profile.ChangeCount == 0 ? None : new Wanted(profile.Name, FlagLayers.Signature(profile));
        }

        private sealed class Marker
        {
            public string Profile { get; set; } = "";
            public string Signature { get; set; } = "";
            public DateTime RestartUtc { get; set; } = DateTime.MinValue;

            /// <summary>Set when the running Roblox was started with a one time profile, for the game below.</summary>
            public bool OneTime { get; set; }
            public long OneTimeUniverseId { get; set; }
            public long OneTimePlaceId { get; set; }
        }

        private static string MarkerPath => Path.Combine(Paths.Base, "AppliedFlagProfile.json");

        private static int _restarting;

        private static Marker Read()
        {
            try
            {
                if (File.Exists(MarkerPath))
                    return JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerPath)) ?? new Marker();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the profile marker: {ex.Message}");
            }

            return new Marker();
        }

        private static void Write(Marker marker)
        {
            try
            {
                File.WriteAllText(MarkerPath, JsonSerializer.Serialize(marker));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write the profile marker: {ex.Message}");
            }
        }

        private static bool CloseRunningOnLaunch => App.Settings.Prop.UseFastFlagManager && App.Settings.Prop.FastFlagPresetCloseRunningRoblox;

        private static readonly HashSet<long> _notifiedPlaces = new();

        public static bool RestartedRecently => (DateTime.UtcNow - Read().RestartUtc).TotalSeconds < 45;

        public static void MarkIntentionalRestart()
        {
            Marker marker = Read();
            marker.RestartUtc = DateTime.UtcNow;
            Write(marker);
        }

        private static bool IsRobloxRunning()
        {
            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private static async Task CloseRobloxAsync()
        {
            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);

            foreach (Process process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                    {
                        if (process.WaitForExit(3000))
                            continue;
                    }

                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not close Roblox ({process.Id}): {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            for (int i = 0; i < 40 && IsRobloxRunning(); i++)
                await Task.Delay(250);
        }

        public static async Task<bool> PrepareLaunchAsync(Wanted wanted, bool gameKnown)
        {
            if (!IsRobloxRunning())
                return true;

            Marker running = Read();

            if (!CloseRunningOnLaunch)
            {
                if (gameKnown && !wanted.IsNone && !string.Equals(wanted.Signature, running.Signature, StringComparison.Ordinal))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox is already running with {Describe(running.Profile, running.Signature)}; this launch wants {Describe(wanted.ProfileName, wanted.Signature)}, which will not apply until Roblox restarts");

                    NotificationCenter.Notify(
                        $"FastFlag profile \"{wanted.ProfileName}\" will not apply",
                        "Roblox is already open, and it only reads FastFlags when it starts. Close Roblox and launch this game again, or turn on closing Roblox on launch in FastFlag settings.",
                        NotificationCategory.General,
                        durationSeconds: 10,
                        kind: NotificationKindId.FastFlagProfile);
                }

                return false;
            }

            if (!gameKnown)
                return false;

            Marker marker = running;
            if (string.Equals(wanted.Signature, marker.Signature, StringComparison.Ordinal))
                return false;

            App.Logger.WriteLine(LOG_IDENT, $"Roblox is already running with {Describe(marker.Profile, marker.Signature)}, this launch needs {Describe(wanted.ProfileName, wanted.Signature)} - closing it so the new flags are read");

            marker.RestartUtc = DateTime.UtcNow;
            Write(marker);

            await CloseRobloxAsync();
            return true;
        }

        public static void NoteLaunchWithoutGame(FlagProfileData profiles)
        {
            if (profiles.Rules.Count == 0)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Roblox is opening without a game, so none of your {profiles.Rules.Count} per-game flag profiles can be picked for this session");

            NotificationCenter.Notify(
                "Per-game FastFlags need a game to launch into",
                "Roblox is opening on its own, so PhasmaStrap cannot tell which game you will pick. Launch the game from the Games page to get its profile.",
                NotificationCategory.General,
                durationSeconds: 10,
                kind: NotificationKindId.FastFlagProfile);
        }

        public static void RecordLaunch(Wanted applied, OneTimeRequest? oneTime = null)
        {
            Marker marker = Read();
            marker.Profile = applied.ProfileName;
            marker.Signature = applied.Signature;
            marker.OneTime = oneTime is not null;
            marker.OneTimeUniverseId = oneTime?.UniverseId ?? 0;
            marker.OneTimePlaceId = oneTime?.PlaceId ?? 0;
            Write(marker);
        }

        #region One time profile

        /// <summary>A flag profile asked for one launch only, left on disk for the launch that follows.</summary>
        public sealed class OneTimeRequest
        {
            public string ProfileId { get; set; } = "";
            public long UniverseId { get; set; }
            public long PlaceId { get; set; }
            public DateTime RequestedUtc { get; set; }
        }

        private static string OneTimePath => Path.Combine(Paths.Base, "OneTimeFlagProfile.json");

        /// <summary>How long a request waits for its launch before it is ignored.</summary>
        private static readonly TimeSpan OneTimeLifetime = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Makes the next launch use <paramref name="profileId"/> instead of the game's own profile, without changing
        /// any saved rule. An empty id means "just your usual flags" for that launch.
        /// </summary>
        public static void RequestOneTime(string profileId, long universeId, long placeId)
        {
            try
            {
                var request = new OneTimeRequest { ProfileId = profileId ?? "", UniverseId = universeId, PlaceId = placeId, RequestedUtc = DateTime.UtcNow };
                File.WriteAllText(OneTimePath, JsonSerializer.Serialize(request));
                App.Logger.WriteLine(LOG_IDENT, $"Next launch of place {placeId} uses {(request.ProfileId.Length > 0 ? $"profile {request.ProfileId}" : "your usual flags")} once");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not save the one time profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Takes the waiting one time request, if there is a fresh one. The request is used up either way.
        /// <paramref name="profile"/> is null for "your usual flags".
        /// </summary>
        public static bool TakeOneTime(FlagProfileData profiles, out OneTimeRequest? request, out FlagProfile? profile)
        {
            request = null;
            profile = null;

            try
            {
                if (!File.Exists(OneTimePath))
                    return false;

                OneTimeRequest? read = JsonSerializer.Deserialize<OneTimeRequest>(File.ReadAllText(OneTimePath));
                File.Delete(OneTimePath);

                if (read is null || DateTime.UtcNow - read.RequestedUtc > OneTimeLifetime)
                    return false;

                profile = read.ProfileId.Length == 0 ? null : profiles.Profiles.FirstOrDefault(p => p.Id == read.ProfileId);
                if (read.ProfileId.Length > 0 && profile is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"The one time profile {read.ProfileId} no longer exists");
                    return false;
                }

                request = read;
                string what = profile is null ? "your usual flags" : "profile " + profile.Name;
                App.Logger.WriteLine(LOG_IDENT, $"Using {what} for this launch only");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the one time profile: {ex.Message}");
                return false;
            }
        }

        #endregion

        public static void OnGameJoined(ActivityData data)
        {
            if (data.PlaceId <= 0)
                return;

            if (data.UniverseId > 0)
                GameLookup.Remember(data.PlaceId, data.UniverseId);

            if (!App.Settings.Prop.UseFastFlagManager)
                return;

            // Roblox was started with a profile picked for this game once: that is what the player asked for.
            Marker started = Read();
            if (started.OneTime && ((started.OneTimeUniverseId > 0 && started.OneTimeUniverseId == data.UniverseId) || started.OneTimePlaceId == data.PlaceId))
                return;

            FlagProfileData profiles = FlagProfileManager.ReadFromDisk();
            FlagProfile? profile = FlagLayers.ProfileFor(profiles, data.PlaceId, data.UniverseId);
            Wanted wanted = Wanted.Of(profile);

            if (wanted.IsNone)
                return;

            bool? inRunning = ProfileInRunningRoblox(profile!);
            if (inRunning == true)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Joined place {data.PlaceId}: profile \"{wanted.ProfileName}\" is in the flags Roblox started with");
                RecordLaunch(wanted);
                return;
            }

            Marker marker = Read();
            if (inRunning is null && string.Equals(wanted.Signature, marker.Signature, StringComparison.Ordinal))
                return;

            lock (_notifiedPlaces)
            {
                if (!_notifiedPlaces.Add(data.PlaceId))
                    return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Joined place {data.PlaceId} with {Describe(marker.Profile, marker.Signature)}; its profile \"{wanted.ProfileName}\" is not active");

            bool canRejoin = data.ServerType == ServerType.Public && !string.IsNullOrEmpty(data.JobId);
            long placeId = data.PlaceId;
            string jobId = data.JobId;

            NotificationCenter.Notify(
                $"FastFlag profile \"{wanted.ProfileName}\" is not active",
                canRejoin
                    ? "Roblox only reads FastFlags when it starts, and it was started with other flags. Click here to restart it into this same server with this game's profile."
                    : "Roblox only reads FastFlags when it starts, and it was started with other flags. Close Roblox and launch this game again to get its profile.",
                NotificationCategory.General,
                durationSeconds: 10,
                onClick: canRejoin ? () => RestartInto(placeId, jobId) : null,
                kind: NotificationKindId.FastFlagProfile);
        }

        private static void RestartInto(long placeId, string jobId)
        {
            if (Interlocked.Exchange(ref _restarting, 1) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Restarting Roblox into place {placeId} / {jobId} on request");

                    MarkIntentionalRestart();
                    await CloseRobloxAsync();

                    RobloxLaunch.Join(placeId, jobId);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Restart failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _restarting, 0);
                }
            });
        }

        private static bool? ProfileInRunningRoblox(FlagProfile profile)
        {
            bool? answer = null;

            foreach (var (folder, started) in ProcessImage.RunningRoblox())
            {
                bool? has = ProfileInFlagsFile(profile, Path.Combine(folder, "ClientSettings", "ClientAppSettings.json"), started);

                if (has == true)
                    return true;
                answer ??= has;
            }

            return answer;
        }

        private static bool? ProfileInFlagsFile(FlagProfile profile, string file, DateTime startedUtc)
        {
            try
            {
                if (!File.Exists(file))
                    return profile.Flags.Count > 0 ? false : null;

                if (File.GetLastWriteTimeUtc(file) > startedUtc.AddSeconds(1))
                    return null;

                var flags = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file)) ?? new();
                var values = flags.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);

                return profile.Flags.All(kv => values.TryGetValue(kv.Key, out string? v) && string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase))
                    && profile.Remove.All(name => !values.ContainsKey(name));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the running Roblox's flags: {ex.Message}");
                return null;
            }
        }

        private static string Describe(string profile, string signature) => signature.Length > 0 ? $"profile \"{profile}\"" : "just your own flags";
    }
}
