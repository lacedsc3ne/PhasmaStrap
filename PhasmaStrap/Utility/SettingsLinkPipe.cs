using System.IO.Pipes;

namespace PhasmaStrap.Utility
{
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
