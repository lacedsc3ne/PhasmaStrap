using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    /// <summary>FPS, ping and Roblox memory sampled through the last game you played. Stored locally only.</summary>
    public sealed class PerformanceSessionSummary
    {
        public long PlaceId { get; set; }
        public long UniverseId { get; set; }
        public string GameName { get; set; } = "";
        public string Region { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public List<int> Fps { get; set; } = new();
        public List<int> PingMs { get; set; } = new();
        public List<int> MemoryMb { get; set; } = new();
    }

    /// <summary>
    /// Runs in the watcher next to the activity tracker. Every few seconds while you are in a game it reads the FPS
    /// the overlay or replay recorder sees, the ping the server ping monitor measures (when the HUD has it on) and
    /// the Roblox player's memory, then writes a summary for the Performance pages when you leave.
    /// </summary>
    public static class PerformanceSessionRecorder
    {
        private const string LOG_IDENT = "PerformanceSessionRecorder";
        private const int SampleSeconds = 5;
        private const int MaxSamples = 720;

        private static readonly object _lock = new();
        private static PerformanceSessionSummary? _current;
        private static System.Threading.Timer? _timer;
        private static bool _following;

        private static string FilePath => Path.Combine(Paths.PlayTime, "LastPerformance.json");

        public static void Follow(ActivityWatcher watcher)
        {
            lock (_lock)
            {
                if (_following)
                    return;
                _following = true;
            }

            watcher.OnGameJoin += (_, _) => Begin(watcher.Data);
            watcher.OnGameLeave += (_, _) => End();
            watcher.OnAppClose += (_, _) => End();

            _timer = new System.Threading.Timer(_ => Sample(), null, SampleSeconds * 1000, SampleSeconds * 1000);
        }

        private static void Begin(ActivityData data)
        {
            End();

            lock (_lock)
            {
                _current = new PerformanceSessionSummary
                {
                    PlaceId = data.PlaceId,
                    UniverseId = data.UniverseId,
                    StartedUtc = DateTime.UtcNow,
                };
            }
        }

        private static void Sample()
        {
            try
            {
                lock (_lock)
                {
                    if (_current is null)
                        return;
                }

                int fps = (int)Math.Round(FpsFeed.Latest);
                int ping = ServerPingMonitor.LatestMs;
                int memoryMb = RobloxMemoryMb();

                // The name and region are cleared when the game ends, so keep the latest known ones while playing.
                string name = NowPlaying.GameName();
                string region = ServerRegion.Current;

                lock (_lock)
                {
                    if (_current is null)
                        return;

                    if (name.Length > 0 && name != "--")
                        _current.GameName = name;
                    if (region.Length > 0)
                        _current.Region = region;

                    if (fps > 0)
                        Add(_current.Fps, fps);
                    if (ping >= 0)
                        Add(_current.PingMs, ping);
                    if (memoryMb > 0)
                        Add(_current.MemoryMb, memoryMb);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Sample failed: {ex.Message}");
            }
        }

        private static void Add(List<int> list, int value)
        {
            list.Add(value);

            // Long sessions: halve the resolution instead of dropping the start.
            if (list.Count > MaxSamples)
            {
                var halved = new List<int>(list.Count / 2 + 1);
                for (int i = 0; i + 1 < list.Count; i += 2)
                    halved.Add((list[i] + list[i + 1]) / 2);
                list.Clear();
                list.AddRange(halved);
            }
        }

        private static int RobloxMemoryMb()
        {
            long peak = 0;
            foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                using (process)
                {
                    try
                    {
                        peak = Math.Max(peak, process.WorkingSet64);
                    }
                    catch
                    {
                    }
                }
            }
            return (int)(peak / 1048576);
        }

        private static void End()
        {
            PerformanceSessionSummary? done;

            lock (_lock)
            {
                done = _current;
                _current = null;
            }

            if (done is null || done.Fps.Count + done.PingMs.Count + done.MemoryMb.Count < 2)
                return;

            try
            {
                done.EndedUtc = DateTime.UtcNow;

                Directory.CreateDirectory(Paths.PlayTime);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(done));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not save the session summary: {ex.Message}");
            }
        }

        /// <summary>The last finished session, or null when nothing was recorded yet.</summary>
        public static PerformanceSessionSummary? LoadLast()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                return JsonSerializer.Deserialize<PerformanceSessionSummary>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the session summary: {ex.Message}");
                return null;
            }
        }
    }
}
