namespace PhasmaStrap.Utility
{
    public static class CrashHandlerStopper
    {
        private const string LOG_IDENT = "CrashHandlerStopper";
        private const string ProcessName = "RobloxCrashHandler";

        private static readonly TimeSpan LookFor = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

        public static async Task RunAsync(Func<bool> robloxStillOpen, TimeSpan? lookFor = null, CancellationToken token = default)
        {
            DateTime giveUp = DateTime.UtcNow + (lookFor ?? LookFor);
            int closed = 0;

            App.Logger.WriteLine(LOG_IDENT, "Watching for Roblox's crash handler to close it");

            try
            {
                while (DateTime.UtcNow < giveUp && !token.IsCancellationRequested && robloxStillOpen())
                {
                    closed += CloseAll();
                    await Task.Delay(Every, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Stopped looking: {ex.Message}");
            }

            App.Logger.WriteLine(LOG_IDENT, closed == 0
                ? "Roblox never started its crash handler this session"
                : $"Closed the crash handler {closed} time(s)");
        }

        public static int CloseAll()
        {
            int closed = 0;

            foreach (Process process in Process.GetProcessesByName(ProcessName))
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    int id = process.Id;
                    process.Kill();
                    process.WaitForExit(2000);
                    closed++;

                    App.Logger.WriteLine(LOG_IDENT, $"Closed {ProcessName} {id}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not close {ProcessName} {process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            return closed;
        }
    }
}
