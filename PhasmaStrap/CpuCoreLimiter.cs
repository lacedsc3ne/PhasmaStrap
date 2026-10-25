using System.Runtime.InteropServices;

namespace PhasmaStrap
{
    public static class CpuCoreLimiter
    {
        private static readonly object Sync = new();

        public static void ApplyConfiguredLimit() => SetCpuCoreLimit(App.Settings.Prop.CpuCoreLimit);

        public static void SetCpuCoreLimit(int coreCount)
        {
            const string LOG_IDENT = "CpuCoreLimiter::SetCpuCoreLimit";

            int processorCount = Environment.ProcessorCount;

            if (processorCount > IntPtr.Size * 8)
                return;

            if (coreCount < 1 || coreCount > processorCount)
                coreCount = processorCount;

            lock (Sync)
            {
                try
                {
                    using Process process = Process.GetCurrentProcess();

                    if (coreCount >= processorCount)
                    {
                        process.ProcessorAffinity = FullMask(process, processorCount);
                        return;
                    }

                    ulong mask = ((1UL << coreCount) - 1UL) << (processorCount - coreCount);
                    process.ProcessorAffinity = (IntPtr)unchecked((long)mask);

                    App.Logger.WriteLine(LOG_IDENT, $"CPU limit set to the top {coreCount} logical processors");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"CPU limit change failed: {ex.Message}");
                }
            }
        }

        public static Process? StartWithoutLimit(ProcessStartInfo startInfo)
        {
            const string LOG_IDENT = "CpuCoreLimiter::StartWithoutLimit";

            int processorCount = Environment.ProcessorCount;

            if (processorCount > IntPtr.Size * 8)
                return Process.Start(startInfo);

            lock (Sync)
            {
                using Process self = Process.GetCurrentProcess();

                IntPtr limited = IntPtr.Zero;
                IntPtr full = FullMask(self, processorCount);

                try
                {
                    if (self.ProcessorAffinity != full)
                    {
                        limited = self.ProcessorAffinity;
                        self.ProcessorAffinity = full;
                        App.Logger.WriteLine(LOG_IDENT, $"Lifted the CPU limit while starting {Path.GetFileName(startInfo.FileName)} so it gets every processor");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not lift the CPU limit before starting {Path.GetFileName(startInfo.FileName)}: {ex.Message}");
                }

                try
                {
                    return Process.Start(startInfo);
                }
                finally
                {
                    if (limited != IntPtr.Zero)
                    {
                        try
                        {
                            self.ProcessorAffinity = limited;
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Could not put the CPU limit back: {ex.Message}");
                        }
                    }
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr process, out UIntPtr processMask, out UIntPtr systemMask);

        private static IntPtr FullMask(Process process, int processorCount)
        {
            if (GetProcessAffinityMask(process.Handle, out _, out UIntPtr systemMask) && systemMask != UIntPtr.Zero)
                return (IntPtr)unchecked((long)systemMask.ToUInt64());

            ulong mask = processorCount >= 64 ? ulong.MaxValue : (1UL << processorCount) - 1UL;
            return (IntPtr)unchecked((long)mask);
        }
    }
}
