namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Name and driver version of the NVIDIA card in this PC, read locally from WMI (Win32_VideoController).
    /// The Windows driver version (for example 32.0.15.6094) is turned into NVIDIA's own form (560.94).
    /// </summary>
    public static class NvidiaDriverInfo
    {
        private const string LOG_IDENT = "NvidiaDriverInfo";

        public sealed record Info(string GpuName, string DriverVersion);

        private static Task<Info?>? _cached;

        public static Task<Info?> GetAsync()
        {
            return _cached ??= Task.Run(Read);
        }

        private static Info? Read()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (System.Management.ManagementBaseObject gpu in searcher.Get())
                {
                    using (gpu)
                    {
                        string name = gpu["Name"] as string ?? "";
                        string pnp = gpu["PNPDeviceID"] as string ?? "";
                        bool nvidia = pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
                        if (!nvidia || name.Length == 0)
                            continue;

                        return new Info(name.Trim(), ToNvidiaVersion(gpu["DriverVersion"] as string ?? ""));
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GPU query failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 32.0.15.6094 becomes 560.94: the last five digits of the third and fourth parts, split 3 and 2.
        /// Returns "" when the text does not look like a Windows driver version.
        /// </summary>
        public static string ToNvidiaVersion(string windowsVersion)
        {
            string[] parts = windowsVersion.Trim().Split('.');
            if (parts.Length < 4 || !parts[2].All(char.IsDigit) || !parts[3].All(char.IsDigit) || parts[2].Length == 0 || parts[3].Length == 0)
                return "";

            string digits = parts[2] + parts[3].PadLeft(4, '0');
            if (digits.Length < 5)
                return "";

            string last = digits[^5..];
            string major = last[..3].TrimStart('0');
            return (major.Length == 0 ? "0" : major) + "." + last[3..];
        }
    }
}
