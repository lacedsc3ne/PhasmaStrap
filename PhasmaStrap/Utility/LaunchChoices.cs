using System.Windows;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// The questions the Deployment page can ask before Roblox starts: "Confirm before joining" for links from the website,
    /// and "Ask which account". Both run on the UI thread in LaunchHandler.LaunchRoblox, before the bootstrapper.
    /// Each returns false when the user called the launch off.
    /// </summary>
    public static class LaunchChoices
    {
        private const string LOG_IDENT = "LaunchChoices";

        /// <summary>The Roblox website starts games with a roblox-player: link that carries a sign in ticket for its own account.</summary>
        public static bool IsWebsiteLaunch(string? launchArgs) =>
            !string.IsNullOrEmpty(launchArgs) && launchArgs.TrimStart('"').StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase);

        /// <summary>The place a launch link points at, or 0.</summary>
        public static long PlaceIdOf(string? launchArgs)
        {
            if (string.IsNullOrEmpty(launchArgs))
                return 0;

            try
            {
                string decoded = Uri.UnescapeDataString(Uri.UnescapeDataString(launchArgs));
                Match match = Regex.Match(decoded, @"placeId=(\d+)", RegexOptions.IgnoreCase);
                return match.Success && long.TryParse(match.Groups[1].Value, out long id) ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>"Confirm before joining": asks before a link from the website starts a game.</summary>
        public static bool ConfirmWebsiteJoin(string? launchArgs)
        {
            if (!App.Settings.Prop.ConfirmWebsiteJoins || !IsWebsiteLaunch(launchArgs))
                return true;

            long placeId = PlaceIdOf(launchArgs);
            string name = placeId > 0 ? PlaceNames.NameOf(placeId) : "";
            string what = name.Length > 0 ? name : placeId > 0 ? $"place {placeId}" : "a game";

            MessageBoxResult result = Frontend.ShowMessageBox(
                $"A link from the Roblox website wants to start {what}.\n\nJoin it now?",
                MessageBoxImage.Question,
                MessageBoxButton.YesNo);

            App.Logger.WriteLine(LOG_IDENT, $"Website join of {what}: {(result == MessageBoxResult.Yes ? "confirmed" : "called off")}");
            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// "Ask which account": shows the saved accounts first and switches the Roblox login to the one picked.
        /// Skipped for website links (their ticket is for the account signed in on the site), with fewer than two saved
        /// accounts, and while a Roblox is open unless "Multi instance" is on.
        /// </summary>
        public static bool AskWhichAccount(string? launchArgs)
        {
            if (!App.Settings.Prop.AskAccountOnLaunch || IsWebsiteLaunch(launchArgs))
                return true;

            List<AccountQuickSwitch.Account> accounts = AccountQuickSwitch.List(Paths.AccountBackups);
            if (accounts.Count < 2)
                return true;

            if (!App.Settings.Prop.AllowMultipleRoblox)
            {
                Process[] running = Process.GetProcessesByName(App.RobloxPlayerAppName);
                bool open = running.Length > 0;
                foreach (Process process in running)
                    process.Dispose();

                if (open)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox is open, launching on the current account without asking");
                    return true;
                }
            }

            var dialog = new UI.Elements.Dialogs.LaunchAccountPickerDialog(accounts);
            dialog.ShowDialog();

            if (dialog.PickedUserId is not long userId)
                return false;

            if (dialog.CurrentUserId == userId)
                return true;

            try
            {
                AccountQuickSwitch.Log ??= message => App.Logger.WriteLine("AccountQuickSwitch", message);

                // Off the UI thread: the switch only copies files, so waiting here cannot deadlock
                Task.Run(() => AccountQuickSwitch.SwitchAsync(Paths.AccountBackups, Integrations.RobloxCookie.LiveCookiesDatPath, userId)).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);

                MessageBoxResult result = Frontend.ShowMessageBox(
                    $"Could not switch accounts: {ex.Message}\n\nLaunch on the account that is signed in now?",
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNo);

                return result == MessageBoxResult.Yes;
            }
        }
    }
}
