using System.IO.Pipes;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Hands a setting link (phasmastrap://settings/...) to the settings window that is already open in another
    /// PhasmaStrap process. Only one settings window runs at a time (the "Settings" <see cref="InterProcessLock"/>),
    /// and that process listens here. Same user and same Windows session only.
    /// </summary>
    internal static class SettingsLinkPipe
    {
        private const string LOG_IDENT = "SettingsLinkPipe";

        private const int MaxLength = 4096;

        private static string PipeName
        {
            get
            {
                using Process current = Process.GetCurrentProcess();
                return $"{App.ProjectName}-SettingsLink-{current.SessionId}";
            }
        }

        private static int _listening;

        /// <summary>Starts listening on a background thread. <paramref name="received"/> runs on that thread.</summary>
        public static void Listen(Action<string> received)
        {
            if (Interlocked.Exchange(ref _listening, 1) == 1)
                return;

            var thread = new Thread(() => Loop(PipeName, received))
            {
                IsBackground = true,
                Name = LOG_IDENT,
            };

            thread.Start();
        }

        private static void Loop(string pipeName, Action<string> received)
        {
            while (true)
            {
                NamedPipeServerStream server;

                try
                {
                    server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                }
                catch (Exception ex)
                {
                    // Another process already owns the name; links will reach that one instead.
                    App.Logger.WriteLine(LOG_IDENT, $"Could not listen for setting links: {ex.Message}");
                    return;
                }

                try
                {
                    using (server)
                    {
                        server.WaitForConnection();

                        using var reader = new StreamReader(server, Encoding.UTF8);
                        char[] buffer = new char[MaxLength];
                        int read = reader.ReadBlock(buffer, 0, buffer.Length);
                        string text = new string(buffer, 0, read).Trim();

                        if (text.Length > 0)
                            received(text);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Setting link pipe error: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }

        /// <summary>Sends <paramref name="link"/> to the open settings window. False when nobody is listening.</summary>
        public static bool Send(string link)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(1500);

                byte[] data = Encoding.UTF8.GetBytes(link.Length > MaxLength ? link[..MaxLength] : link);
                client.Write(data, 0, data.Length);
                client.Flush();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not pass the link on: {ex.Message}");
                return false;
            }
        }
    }
}
