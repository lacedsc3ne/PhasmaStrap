namespace PhasmaStrap.Utility.Backend
{
    /// <summary>A short report about a crash of PhasmaStrap itself, sent only while "Send crash reports" is on.</summary>
    public sealed class CrashReportBody
    {
        /// <summary>PhasmaStrap version, for example "3.4.0".</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        /// <summary>"Stable" or "Beta", the update channel the user is on.</summary>
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "";

        /// <summary>Windows version, for example "Microsoft Windows NT 10.0.22631.0".</summary>
        [JsonPropertyName("os")]
        public string Os { get; set; } = "";

        /// <summary>Exception type, for example "System.NullReferenceException".</summary>
        [JsonPropertyName("exception_type")]
        public string ExceptionType { get; set; } = "";

        /// <summary>Exception.ToString(): message plus stack trace, inner exceptions included. Capped at 16 000 characters.</summary>
        [JsonPropertyName("exception")]
        public string Exception { get; set; } = "";

        /// <summary>The last log lines before the crash (at most 200 lines, 32 000 characters), oldest first.</summary>
        [JsonPropertyName("log_tail")]
        public string LogTail { get; set; } = "";

        /// <summary>What PhasmaStrap was doing: "player", "studio", "settings", "watcher" or "other".</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "";

        /// <summary>When it happened, Unix seconds UTC.</summary>
        [JsonPropertyName("at")]
        public long At { get; set; }
    }

    /// <summary>
    /// Extras area calls to api.phasmastrap.com (see specs/extras.md). Fails soft like every PhasmaApi call.
    /// </summary>
    public static class ExtrasApi
    {
        private const string LOG_IDENT = "ExtrasApi";
        private const int MaxLogLines = 200;
        private const int MaxLogChars = 32000;
        private const int MaxExceptionChars = 16000;

        private static int _sent;

        /// <summary>
        /// Uploads a crash report: POST /v1/crash. Public (no sign in needed); the account header is sent when signed in.
        /// Only the first crash of a process is sent. Never throws.
        /// </summary>
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
