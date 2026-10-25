namespace PhasmaStrap.Utility
{
    public static class RobloxLaunch
    {
        private const string LOG_IDENT = "RobloxLaunch";

        public static string DeepLink(long placeId, string? jobId = null, string? accessCode = null, string? linkCode = null, string? launchData = null)
        {
            if (placeId <= 0)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Refusing to build a link for place id {placeId}");
                return "";
            }

            var uri = new StringBuilder($"roblox://experiences/start?placeId={placeId}");

            if (!string.IsNullOrEmpty(accessCode))
                uri.Append($"&accessCode={Uri.EscapeDataString(accessCode)}");
            else if (!string.IsNullOrEmpty(linkCode))
                uri.Append($"&linkCode={Uri.EscapeDataString(linkCode)}");
            else if (!string.IsNullOrEmpty(jobId))
            {
                if (Guid.TryParse(jobId, out Guid server))
                    uri.Append($"&gameInstanceId={Uri.EscapeDataString(server.ToString())}");
                else
                    App.Logger.WriteLine(LOG_IDENT, $"Ignoring server id \"{jobId}\" for place {placeId}, it is not a valid server id");
            }

            if (!string.IsNullOrEmpty(launchData))
                uri.Append($"&launchData={Uri.EscapeDataString(launchData)}");

            return uri.ToString();
        }

        public static string FollowLink(long userId) => $"roblox://experiences/start?userId={userId}";

        public static void StartPlayer(string executable, string uri)
        {
            var startInfo = new ProcessStartInfo(executable);
            startInfo.ArgumentList.Add("-player");
            startInfo.ArgumentList.Add(uri);

            using Process? process = Process.Start(startInfo);
        }

        public static bool Launch(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                App.Logger.WriteLine(LOG_IDENT, "Nothing to launch, the link is empty");
                return false;
            }

            if (!uri.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase) && !uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Refusing to launch {uri}, it is not a Roblox link");
                return false;
            }

            try
            {
                StartPlayer(Paths.Process, uri);

                // Deployment > "Close PhasmaStrap" set to Minimise to tray: also when joining from inside the app
                if (App.Settings.Prop.SettingsWindowOnLaunch == "Tray")
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        System.Windows.Application.Current.Windows.OfType<UI.Elements.Settings.MainWindow>().FirstOrDefault()?.HideToTray()));

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not launch {uri}: {ex.Message}");
                return false;
            }
        }

        public static bool Join(long placeId, string? jobId = null) => Launch(DeepLink(placeId, jobId));
    }
}
