namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>
    /// Turns the raw samples of the Connection and Stutter tools into a row of bars.
    /// Long sample lists are binned so a chart never has more bars than fit comfortably.
    /// </summary>
    internal static class DiagnosticsCharts
    {
        public const double Height = 48;

        private const int MaxPingBars = 60;
        private const int MaxFrameBars = 90;

        /// <summary>Ping samples in milliseconds, with -1 for a ping that got no answer.</summary>
        public static List<DiagnosticsBar> PingBars(List<int> samples)
        {
            var bars = new List<DiagnosticsBar>();
            if (samples.Count == 0)
                return bars;

            int count = Math.Min(MaxPingBars, samples.Count);
            var bins = new List<(double Average, bool Lost, int LostCount, int Size)>(count);

            for (int i = 0; i < count; i++)
            {
                int from = i * samples.Count / count;
                int to = Math.Max(from + 1, (i + 1) * samples.Count / count);
                List<int> slice = samples.GetRange(from, to - from);
                List<int> answered = slice.Where(s => s >= 0).ToList();

                bins.Add((answered.Count > 0 ? answered.Average() : 0, answered.Count < slice.Count, slice.Count - answered.Count, slice.Count));
            }

            List<double> answeredAverages = bins.Where(b => !b.Lost).Select(b => b.Average).OrderBy(v => v).ToList();
            double median = answeredAverages.Count > 0 ? answeredAverages[answeredAverages.Count / 2] : 0;
            double top = Math.Max(20, bins.Max(b => b.Average)) * 1.1;

            foreach (var bin in bins)
            {
                if (bin.Lost)
                {
                    bars.Add(new DiagnosticsBar
                    {
                        Height = Height,
                        Kind = ChartBarKind.Lost,
                        Tip = bin.LostCount == bin.Size ? "No answer" : $"{bin.LostCount} of {bin.Size} pings got no answer",
                    });
                    continue;
                }

                bool spike = median > 0 && bin.Average > median * 1.5 && bin.Average - median > 15;

                bars.Add(new DiagnosticsBar
                {
                    Height = Math.Max(3, bin.Average / top * Height),
                    Kind = spike ? ChartBarKind.Spike : ChartBarKind.Normal,
                    Tip = $"{bin.Average:0} ms",
                });
            }

            return bars;
        }

        /// <summary>The worst frame time of each slice of a measurement, with stutters marked.</summary>
        public static List<DiagnosticsBar> FrameBars(PerformanceReport report)
        {
            var bars = new List<DiagnosticsBar>();
            List<double> data = report.WorstPerSlice;

            if (data.Count == 0)
                return bars;

            int count = Math.Min(MaxFrameBars, data.Count);
            double secondsPerSlice = report.Seconds > 0 ? report.Seconds / data.Count : 0;

            double top = Math.Clamp(data.OrderBy(d => d).ElementAt(Math.Min(data.Count - 1, (int)(data.Count * 0.98))) * 1.6, report.MedianFrameMs * 3, 250);
            top = Math.Max(top, 20);

            double stutterLine = Math.Max(25, report.MedianFrameMs * 2.5);

            for (int i = 0; i < count; i++)
            {
                int from = i * data.Count / count;
                int to = Math.Max(from + 1, (i + 1) * data.Count / count);
                double worst = data.GetRange(from, to - from).Max();

                double startSecond = from * secondsPerSlice;
                double endSecond = to * secondsPerSlice;

                bool stutter = secondsPerSlice > 0
                    ? report.Stutters.Any(s => s.Second >= startSecond && s.Second < endSecond)
                    : worst >= stutterLine;

                bars.Add(new DiagnosticsBar
                {
                    Height = Math.Max(3, Math.Min(worst, top) / top * Height),
                    Kind = stutter ? ChartBarKind.Spike : ChartBarKind.Normal,
                    Tip = secondsPerSlice > 0
                        ? $"{startSecond:0} s: slowest frame {worst:0} ms"
                        : $"Slowest frame {worst:0} ms",
                });
            }

            return bars;
        }
    }
}
