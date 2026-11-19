namespace PhasmaStrap.Utility.Backend
{
    public sealed class CrashReportBody
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "";

        [JsonPropertyName("os")]
        public string Os { get; set; } = "";

        [JsonPropertyName("exception_type")]
        public string ExceptionType { get; set; } = "";

        [JsonPropertyName("exception")]
        public string Exception { get; set; } = "";

        [JsonPropertyName("log_tail")]
        public string LogTail { get; set; } = "";

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "";

        [JsonPropertyName("at")]
        public long At { get; set; }
    }

    public static class ExtrasApi
    {
        private const string LOG_IDENT = "ExtrasApi";
        private const int MaxLogLines = 200;
        private const int MaxLogChars = 32000;
        private const int MaxExceptionChars = 16000;

        private static int _sent;

        public static async Task<bool> SendCrashReportAsync(Exception ex)
        {
            if (!App.Settings.Prop.SendCrashReports || Interlocked.Exchange(ref _sent, 1) == 1)
                return false;

            try
            {
                string exception = ex.ToString();
                if (exception.Length > MaxExceptionChars)
                    exception = exception[..MaxExceptionChars];

                var body = new CrashReportBody
                {
                    Version = App.Version,
                    Channel = App.Settings.Prop.AppUpdateChannel,
                    Os = Environment.OSVersion.VersionString,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    Exception = exception,
                    LogTail = LogTail(),
                    Mode = Mode(),
                    At = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };

                bool ok = await PhasmaApi.PostAsync("/v1/crash", body, signedInOnly: false).ConfigureAwait(false);
                App.Logger.WriteLine(LOG_IDENT, ok ? "Crash report sent" : "Crash report was not accepted");
                return ok;
            }
            catch (Exception sendEx)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not send the crash report: {sendEx.Message}");
                return false;
            }
        }

        private static string LogTail()
        {
            try
            {
                string[] lines;
                lock (App.Logger.History)
                    lines = App.Logger.History.ToArray();

                string tail = String.Join('\n', lines.Skip(Math.Max(0, lines.Length - MaxLogLines)));
                return tail.Length > MaxLogChars ? tail[^MaxLogChars..] : tail;
            }
            catch
            {
                return "";
            }
        }

        private static string Mode()
        {
            try
            {
                if (App.LaunchSettings is null)
                    return "other";

                if (App.LaunchSettings.WatcherFlag.Active)
                    return "watcher";

                return App.LaunchSettings.RobloxLaunchMode switch
                {
                    LaunchMode.Player => "player",
                    LaunchMode.Studio or LaunchMode.StudioAuth => "studio",
                    _ => App.LaunchSettings.MenuFlag.Active ? "settings" : "other",
                };
            }
            catch
            {
                return "other";
            }
        }
    }
}
