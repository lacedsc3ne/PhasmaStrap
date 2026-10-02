using System.Runtime;
using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    public static class MemoryReport
    {
        private const string LOG_IDENT = "MemoryReport";

        private static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan TrimEvery = TimeSpan.FromMinutes(5);

        private static readonly Dictionary<string, Func<long>> _sources = new();

        private static CancellationTokenSource? _cts;

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        public static void Track(string name, Func<long> bytes)
        {
            lock (_sources)
                _sources[name] = bytes;
        }

        public static void Forget(string name)
        {
            lock (_sources)
                _sources.Remove(name);
        }

        public static void Start(bool trimWhenIdle)
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _ = Task.Run(async () =>
            {
                DateTime lastTrim = DateTime.UtcNow;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(ReportEvery, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        Write();

                        if (trimWhenIdle && DateTime.UtcNow - lastTrim >= TrimEvery)
                        {
                            lastTrim = DateTime.UtcNow;
                            Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not measure memory: {ex.Message}");
                    }
                }
            });
        }

        public static void Write()
        {
            using Process self = Process.GetCurrentProcess();
            self.Refresh();

            GCMemoryInfo gc = GC.GetGCMemoryInfo();

            var parts = new List<string>
            {
                $"working set {Megabytes(self.WorkingSet64)}",
                $"private {Megabytes(self.PrivateMemorySize64)}",
                $"managed heap {Megabytes(GC.GetTotalMemory(false))}",
                $"heap committed {Megabytes(gc.TotalCommittedBytes)}",
                $"{self.Threads.Count} threads",
                $"{self.HandleCount} handles",
            };

            lock (_sources)
            {
                foreach ((string name, Func<long> bytes) in _sources)
                {
                    try
                    {
                        parts.Add($"{name} {Megabytes(bytes())}");
                    }
                    catch (Exception)
                    {
                        parts.Add($"{name} unknown");
                    }
                }
            }

            App.Logger.WriteLine(LOG_IDENT, string.Join(", ", parts));
        }

        public static void Trim()
        {
            using Process self = Process.GetCurrentProcess();
            long before = self.WorkingSet64;

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            EmptyWorkingSet(self.Handle);

            self.Refresh();
            App.Logger.WriteLine(LOG_IDENT, $"Handed idle memory back to Windows: {Megabytes(before)} down to {Megabytes(self.WorkingSet64)}");
        }

        private static string Megabytes(long bytes) => $"{bytes / 1048576.0:0} MB";
    }
}
