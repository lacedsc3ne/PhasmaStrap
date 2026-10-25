namespace PhasmaStrap.Integrations
{
    public static class ClassicServerManager
    {
        private const string LOG_IDENT = "ClassicServerManager";

        private static readonly object s_lock = new();

        private static Process? s_serverProcess;

        private static Thread? s_redirectHolderThread;

        private static int s_launchCount;

        public static bool IsRunning
        {
            get
            {
                lock (s_lock)
                {
                    return s_serverProcess is { HasExited: false };
                }
            }
        }

        private static int CurrentLaunch()
        {
            lock (s_lock)
            {
                return s_launchCount;
            }
        }

        public static string? Start(string client)
        {
            const string LOG_IDENT_LOCAL = LOG_IDENT + "::Start";

            if (!App.Settings.Prop.ClassicClientEnabled)
                return "The classic client / private server feature is disabled in settings.";

            if (!ClassicClients.IsSupportedClientCode(client))
            {
                App.Logger.WriteLine(LOG_IDENT_LOCAL, $"Refused to start the server for invalid client code '{client}'");
                return "The selected classic client is not valid. Pick a client from the list and try again.";
            }

            lock (s_lock)
            {
                if (s_serverProcess is { HasExited: false })
                {
                    App.Logger.WriteLine(LOG_IDENT_LOCAL, $"Server already running (pid {s_serverProcess.Id}), reusing it for client {client}");
                }
                else if (!ClassicClients.ServerEngineInstalled)
                {
                    return "The classic private server engine (PhasmaStrap.Server.exe) was not found at " + ClassicClients.ServerPath + ".";
                }
                else
                {
                    try
                    {
                        var startInfo = new ProcessStartInfo(ClassicClients.ServerPath, $"--client={client} --pid={Environment.ProcessId}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = ClassicClients.Root
                        };

                        s_serverProcess = Process.Start(startInfo);
                        if (s_serverProcess is null)
                            return "Failed to start the classic private server process.";

                        App.Logger.WriteLine(LOG_IDENT_LOCAL, $"Started {ClassicClients.ServerExecutableName} for client {client} (pid {s_serverProcess.Id})");
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
                        return "Failed to start the classic private server process: " + ex.Message;
                    }
                }
            }

            lock (s_lock)
                s_launchCount++;

            string? redirectError = ClassicHostRedirect.IsApplied() ? null : ClassicHostRedirect.Set(true);
            if (redirectError != null)
            {
                App.Logger.WriteLine(LOG_IDENT_LOCAL, "Redirect failed, stopping server process: " + redirectError);
                Stop();
                return redirectError;
            }

            lock (s_lock)
            {
                if (s_redirectHolderThread is { IsAlive: true })
                {
                    App.Logger.WriteLine(LOG_IDENT_LOCAL, "The redirect holder from the last launch is still watching, it will cover this client too");
                }
                else
                {
                    s_redirectHolderThread = new Thread(() => ClassicHostRedirect.RemoveWhenSessionEnds(CurrentLaunch))
                    {
                        IsBackground = true,
                        Name = "ClassicRedirectHolder"
                    };
                    s_redirectHolderThread.Start();
                    App.Logger.WriteLine(LOG_IDENT_LOCAL, "Started the redirect holder for this session");
                }
            }

            return null;
        }

        public static void Stop()
        {
            const string LOG_IDENT_LOCAL = LOG_IDENT + "::Stop";

            lock (s_lock)
            {
                if (s_serverProcess is not null)
                {
                    try
                    {
                        if (!s_serverProcess.HasExited)
                        {
                            s_serverProcess.Kill(entireProcessTree: true);
                            s_serverProcess.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
                    }
                    finally
                    {
                        s_serverProcess.Dispose();
                        s_serverProcess = null;
                    }
                }
            }

            try
            {
                ClassicHostRedirect.Set(false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
            }
        }
    }
}
