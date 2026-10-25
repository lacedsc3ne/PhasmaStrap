namespace PhasmaStrap.Networking
{
    public static class NetworkingController
    {
        private const string LOG_IDENT = "NetworkingController";

        private static System.Threading.Timer? _keeper;

        private static int _keeperBusy;

        public static bool IsActive => ProxyHealth.IsHostedAnywhere() && HostsFileManager.IsBlockPresent();

        public static bool EnsureCertificateInstalled()
        {
            bool storeOk = AssetProxyCA.IsInstalledInTrustStore();

            if (!storeOk)
            {
                storeOk = AssetProxyCA.InstallToTrustStore();
                App.Logger.WriteLine(LOG_IDENT, storeOk ? "Installed the proxy root certificate" : "Failed to install the proxy root certificate");
            }

            try
            {
                AssetProxyCA.PatchRobloxTrustBundles();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not patch Roblox's trust bundle: {ex.Message}");
            }

            return storeOk;
        }

        public static bool Enable()
        {
            RegisterHosts();
            AssetProxyServer.Start();

            if (!AssetProxyServer.IsRunning)
            {
                App.Logger.WriteLine(LOG_IDENT, "Proxy server failed to start, aborting enable");
                return false;
            }

            if (!EnsureCertificateInstalled())
            {
                App.Logger.WriteLine(LOG_IDENT, "Certificate install failed, rolling back");
                AssetProxyServer.Stop();
                return false;
            }

            bool hostsOk = HostsFileManager.IsBlockCurrent() || HostsElevation.Apply(true, App.Settings.Prop.BlockRobloxTelemetry);
            if (!hostsOk)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hosts file install was declined or failed, rolling back");
                AssetProxyServer.Stop();
                return false;
            }

            App.Settings.Prop.NetworkingProxyEnabled = true;
            App.Settings.Save();

            UsernameSpoofer.WarmUpIdentity();
            StartKeeper();
            return true;
        }

        public static bool Disable()
        {
            StopKeeper();

            bool hostsOk = HostsElevation.Apply(false, App.Settings.Prop.BlockRobloxTelemetry) || !HostsFileManager.IsBlockPresent();
            if (!hostsOk)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hosts file removal was declined or failed, keeping the proxy running so Roblox still connects");
                StartKeeper();
                return false;
            }

            AssetProxyServer.Stop();

            try
            {
                AssetProxyCA.UnpatchRobloxTrustBundles();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not restore Roblox's trust bundle: {ex.Message}");
            }

            bool certificateRemoved = AssetProxyCA.RemoveFromTrustStore();
            App.Logger.WriteLine(LOG_IDENT, certificateRemoved ? "Removed the proxy root certificate" : "Could not remove the proxy root certificate");

            App.Settings.Prop.NetworkingProxyEnabled = false;
            App.Settings.Save();
            return true;
        }

        public static void ReconcileOnStartup(bool allowPrompts)
        {
            if (App.Settings.Prop.NetworkingProxyEnabled)
            {
                RegisterHosts();
                AssetProxyServer.Start();

                if (allowPrompts)
                    EnsureCertificateInstalled();

                UsernameSpoofer.WarmUpIdentity();
                StartKeeper();
            }
            else if (allowPrompts && AssetProxyCA.IsAnyRootInTrustStore())
            {
                App.Logger.WriteLine(LOG_IDENT, "The proxy is off but its root certificate is still trusted, removing it");
                AssetProxyCA.RemoveFromTrustStore();
            }

            if (allowPrompts)
                HostsElevation.ReconcileOnStartup();
            else
                App.Logger.WriteLine(LOG_IDENT, "Skipping certificate and hosts checks that could show a Windows prompt, this is not the settings window");
        }

        private static void StartKeeper()
        {
            lock (typeof(NetworkingController))
            {
                _keeper ??= new System.Threading.Timer(_ => KeeperTick(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
            }
        }

        private static void StopKeeper()
        {
            lock (typeof(NetworkingController))
            {
                _keeper?.Dispose();
                _keeper = null;
            }
        }

        private static void KeeperTick()
        {
            if (Interlocked.CompareExchange(ref _keeperBusy, 1, 0) != 0)
                return;

            try
            {
                if (!App.Settings.Prop.NetworkingProxyEnabled)
                    return;

                if (!AssetProxyServer.IsRunning)
                {
                    RegisterHosts();
                    AssetProxyServer.Start();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Keeper tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _keeperBusy, 0);
            }
        }

        private static void RegisterHosts()
        {
            AssetProxyServer.InterceptedHosts[PresenceSpoofPolicy.Host] = (PresenceSpoofPolicy.TransformRequest, UsernameSpoofer.ProcessResponse, PresenceSpoofPolicy.TryServeFromCache);
            AssetProxyServer.InterceptedHosts[RobuxSpoofer.Host] = (null, RobuxSpoofer.ProcessResponse, null);
            AssetProxyServer.InterceptedHosts[GameCreatorSpoofer.Host] = (null, GameCreatorSpoofer.ProcessResponse, null);
            AssetProxyServer.InterceptedHosts[AssetWarpPolicy.Host] = (AssetWarpPolicy.TransformRequest, CachePreloadThenRewriteBatch, ServeBatchFromPreloadCache);
            AssetProxyServer.InterceptedHosts[AssetWarpThumbnailPolicy.Host] = (null, AssetWarpThumbnailPolicy.ProcessResponse, null);

            AssetProxyServer.AsyncHandlers[AssetWarpPolicy.Host] = AssetContentService.HandleAsync;
            AssetProxyServer.AsyncHandlers[JoinPickerPolicy.Host] = JoinPickerPolicy.HandleAsync;
        }

        private static byte[]? CachePreloadThenRewriteBatch(ProxiedRequest request, ProxiedResponse response)
        {
            byte[]? cached = AssetPreloadCache.CacheResponse(request, response);
            ProxiedResponse intermediate = cached is null ? response : response with { Body = cached };
            return AssetContentService.RewriteBatch(request, intermediate) ?? cached;
        }

        private static ProxiedResponse? ServeBatchFromPreloadCache(ProxiedRequest request)
        {
            ProxiedResponse? cached = AssetPreloadCache.TryServeFromCache(request);
            if (cached is null)
                return null;

            byte[]? rewritten = AssetContentService.RewriteBatch(request, cached);
            return rewritten is null ? cached : cached with { Body = rewritten };
        }
    }
}
