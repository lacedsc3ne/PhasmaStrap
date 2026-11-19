using System.Windows;

namespace PhasmaStrap.UI
{
    internal static class SettingsWindowState
    {
        private static readonly List<Action> s_pending = new();

        public static bool IsShown
        {
            get
            {
                if (Application.Current is null)
                    return false;

                foreach (Window window in Application.Current.Windows)
                    if (window is Elements.Settings.MainWindow && window.IsVisible && window.WindowState != System.Windows.WindowState.Minimized)
                        return true;

                return false;
            }
        }

        public static void RunWhenShown(Action action)
        {
            if (IsShown)
            {
                action();
                return;
            }

            if (!s_pending.Contains(action))
                s_pending.Add(action);
        }

        internal static void NotifyShown()
        {
            if (s_pending.Count == 0 || !IsShown)
                return;

            Action[] run = s_pending.ToArray();
            s_pending.Clear();

            foreach (Action action in run)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("SettingsWindowState", $"Deferred refresh failed: {ex.Message}");
                }
            }
        }
    }
}
