using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PhasmaStrap.Networking
{
    public static class AssetProxyCA
    {
        private const string LOG_IDENT = "AssetProxyCA";

        private const string SubjectName = "CN=PhasmaStrap Local Proxy CA";

        private const string NameConstraintsOid = "2.5.29.30";

        private static readonly string[] PermittedDomains = { "roblox.com", "rbxcdn.com" };

        private static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("PhasmaStrap Local Proxy CA");

        private static string CertificateFolder => Path.Combine(Paths.LocalAppData, "PhasmaStrap", "AssetProxy");

        private static string CertificateFile => Path.Combine(CertificateFolder, "ca.dat");

        private static string LegacyCertificateFile => Path.Combine(CertificateFolder, "ca.pfx");

        private static X509Certificate2? _cached;

        private static DateTime _cachedWriteTimeUtc;

        private static readonly Dictionary<string, X509Certificate2> LeafCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new();

        public static X509Certificate2 GetOrCreateRootCertificate()
        {
            lock (Sync)
            {
                X509Certificate2? existing = LoadRoot();
                if (existing is not null)
                    return existing;

                string path = CertificateFile;
                var created = CreateRootCertificate(out byte[] pfx);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, ProtectedData.Protect(pfx, KeyEntropy, DataProtectionScope.CurrentUser));
                CryptographicOperations.ZeroMemory(pfx);
                App.Logger.WriteLine(LOG_IDENT, $"Generated new local proxy CA limited to {string.Join(", ", PermittedDomains)}, valid until {created.NotAfter}");

                DeleteLegacyCertificateFile();

                ForgetDerived();
                _cached = created;
                _cachedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                return created;
            }
        }

        private static void DeleteLegacyCertificateFile()
        {
            try
            {
                if (!File.Exists(LegacyCertificateFile))
                    return;

                File.Delete(LegacyCertificateFile);
                App.Logger.WriteLine(LOG_IDENT, $"Deleted the old unprotected proxy CA file {LegacyCertificateFile}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not delete the old proxy CA file {LegacyCertificateFile}: {ex.Message}");
            }
        }

        private static byte[] BuildNameConstraints()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            var permittedSubtrees = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            var dnsName = new Asn1Tag(TagClass.ContextSpecific, 2);

            writer.PushSequence();
            writer.PushSequence(permittedSubtrees);

            foreach (string domain in PermittedDomains)
            {
                writer.PushSequence();
                writer.WriteCharacterString(UniversalTagNumber.IA5String, domain, dnsName);
                writer.PopSequence();
            }

            writer.PopSequence(permittedSubtrees);
            writer.PopSequence();

            return writer.Encode();
        }

        private static bool HasCurrentNameConstraints(X509Certificate2 certificate)
        {
            X509Extension? extension = certificate.Extensions[NameConstraintsOid];
            return extension is not null && extension.Critical && extension.RawData.AsSpan().SequenceEqual(BuildNameConstraints());
        }

        public static bool IsPermittedHost(string? hostname)
        {
            if (string.IsNullOrEmpty(hostname))
                return false;

            foreach (string domain in PermittedDomains)
            {
                if (hostname.Equals(domain, StringComparison.OrdinalIgnoreCase) || hostname.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static X509Certificate2? LoadRoot()
        {
            lock (Sync)
            {
                string path = CertificateFile;
                if (!File.Exists(path))
                    return null;

                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (_cached is not null && writeTime == _cachedWriteTimeUtc)
                    return _cached;

                try
                {
                    byte[] pfx = ProtectedData.Unprotect(File.ReadAllBytes(path), KeyEntropy, DataProtectionScope.CurrentUser);
                    var existing = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
                    CryptographicOperations.ZeroMemory(pfx);

                    if (existing.NotAfter <= DateTime.Now.AddDays(7))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"The proxy CA expires on {existing.NotAfter}, making a new one");
                        existing.Dispose();
                        return null;
                    }

                    if (!HasCurrentNameConstraints(existing))
                    {
                        App.Logger.WriteLine(LOG_IDENT, "The stored proxy CA is not limited to the Roblox domains, making a new one");
                        existing.Dispose();
                        return null;
                    }

                    if (_cached is not null)
                        App.Logger.WriteLine(LOG_IDENT, "The proxy CA was replaced by another PhasmaStrap process - using the new one");

                    ForgetDerived();
                    _cached = existing;
                    _cachedWriteTimeUtc = writeTime;
                    return existing;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Existing CA could not be loaded, regenerating: {ex.Message}");
                    return null;
                }
            }
        }

        private static void ForgetDerived()
        {
            LeafCache.Clear();
            _rootPemCached = null;
            _trustStoreCached = null;
            BundleStateCache.Clear();
        }

        private static X509Certificate2 CreateRootCertificate(out byte[] pfx)
        {
            using RSA rsa = RSA.Create(2048);

            var request = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(new X509Extension(NameConstraintsOid, BuildNameConstraints(), true));

            using X509Certificate2 cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(2));

            pfx = cert.Export(X509ContentType.Pfx);
            return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
        }

        public static X509Certificate2 GetLeafCertificate(string hostname)
        {
            if (!IsPermittedHost(hostname))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Refusing to make a certificate for {hostname}, it is outside {string.Join(", ", PermittedDomains)}");
                throw new ArgumentException($"{hostname} is not a Roblox host", nameof(hostname));
            }

            lock (Sync)
            {
                X509Certificate2 root = GetOrCreateRootCertificate();

                if (LeafCache.TryGetValue(hostname, out var existing) && existing.NotAfter > DateTime.Now.AddDays(1))
                    return existing;

                using RSA rsa = RSA.Create(2048);
                var request = new CertificateRequest($"CN={hostname}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName(hostname);
                request.CertificateExtensions.Add(sanBuilder.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

                byte[] serial = RandomNumberGenerator.GetBytes(16);

                DateTimeOffset notAfter = DateTimeOffset.Now.AddYears(1);
                DateTimeOffset rootEnd = new DateTimeOffset(root.NotAfter).AddMinutes(-1);
                if (notAfter > rootEnd)
                    notAfter = rootEnd;

                using X509Certificate2 leaf = request.Create(root, DateTimeOffset.Now.AddDays(-1), notAfter, serial);
                using X509Certificate2 leafWithKey = leaf.CopyWithPrivateKey(rsa);

                var usable = new X509Certificate2(leafWithKey.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.DefaultKeySet);
                LeafCache[hostname] = usable;
                return usable;
            }
        }

        private static bool? _trustStoreCached;
        private static DateTime _trustStoreCheckedUtc;

        public static bool IsInstalledInTrustStore()
        {
            lock (Sync)
            {
                X509Certificate2? root = LoadRoot();
                if (root is null)
                    return false;

                if (_trustStoreCached.HasValue && (DateTime.UtcNow - _trustStoreCheckedUtc).TotalSeconds < 10)
                    return _trustStoreCached.Value;

                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                _trustStoreCached = store.Certificates.Find(X509FindType.FindByThumbprint, root.Thumbprint, false).Count > 0;
                _trustStoreCheckedUtc = DateTime.UtcNow;
                return _trustStoreCached.Value;
            }
        }

        public static bool InstallToTrustStore()
        {
            try
            {
                X509Certificate2 root = GetOrCreateRootCertificate();
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);

                RemoveOurRoots(store, root.Thumbprint);

                using var publicOnly = new X509Certificate2(root.RawData);
                store.Add(publicOnly);
                lock (Sync) { _trustStoreCached = true; _trustStoreCheckedUtc = DateTime.UtcNow; }
                App.Logger.WriteLine(LOG_IDENT, $"Root CA {root.Thumbprint} installed to CurrentUser trust store");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static bool RemoveFromTrustStore()
        {
            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                RemoveOurRoots(store, null);
                lock (Sync) { _trustStoreCached = false; _trustStoreCheckedUtc = DateTime.UtcNow; }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static bool IsAnyRootInTrustStore()
        {
            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, SubjectName, false).Count > 0;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the CurrentUser trust store: {ex.Message}");
                return false;
            }
        }

        private static void RemoveOurRoots(X509Store store, string? keepThumbprint)
        {
            foreach (X509Certificate2 found in store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, SubjectName, false))
            {
                using (found)
                {
                    if (keepThumbprint is not null && found.Thumbprint.Equals(keepThumbprint, StringComparison.OrdinalIgnoreCase))
                        continue;

                    store.Remove(found);
                    App.Logger.WriteLine(LOG_IDENT, $"Root CA {found.Thumbprint} (valid until {found.NotAfter}) removed from CurrentUser trust store");
                }
            }
        }

        private const string BundleMarker = "# PhasmaStrap Local Proxy CA - added automatically, removed when the proxy is disabled";

        private static string? _rootPemCached;

        private static string RootPem() => RootPem(GetOrCreateRootCertificate());

        private static string RootPem(X509Certificate2 root)
        {
            lock (Sync)
            {
                if (_rootPemCached is not null && ReferenceEquals(root, _cached))
                    return _rootPemCached;
                string base64 = Convert.ToBase64String(root.Export(X509ContentType.Cert));

                var sb = new StringBuilder();
                sb.AppendLine("-----BEGIN CERTIFICATE-----");
                for (int i = 0; i < base64.Length; i += 64)
                    sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
                sb.Append("-----END CERTIFICATE-----");
                _rootPemCached = sb.ToString();
                return _rootPemCached;
            }
        }

        private static readonly Dictionary<string, (DateTime WriteTimeUtc, bool Patched)> BundleStateCache = new(StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<string> BundlesThatMatter()
        {
            List<string> own = FindTrustBundles(Paths.Versions).ToList();

            return own.Count > 0 ? own : FindTrustBundles().ToList();
        }

        private static IEnumerable<string> FindTrustBundles(params string[] only)
        {
            string[] roots = only.Length > 0 ? only : new[] { Paths.Versions, Path.Combine(Paths.LocalAppData, "Roblox", "Versions") };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch (Exception) { continue; }

                foreach (string dir in dirs)
                {
                    string bundle = Path.Combine(dir, "ssl", "cacert.pem");
                    if (File.Exists(bundle) && seen.Add(Path.GetFullPath(bundle)))
                        yield return bundle;
                }
            }
        }

        private static string StripOwnBlock(string content)
        {
            int index;
            while ((index = content.IndexOf(BundleMarker, StringComparison.Ordinal)) >= 0)
            {
                const string End = "-----END CERTIFICATE-----";
                int end = content.IndexOf(End, index, StringComparison.Ordinal);
                if (end < 0)
                {
                    content = content[..index];
                    break;
                }

                content = content[..index] + content[(end + End.Length)..];
            }

            return content.TrimEnd();
        }

        public static int PatchRobloxTrustBundles()
        {
            int patched = 0;
            string pem;

            try { pem = RootPem(); }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return 0;
            }

            foreach (string bundle in FindTrustBundles())
            {
                try
                {
                    string original = File.ReadAllText(bundle);
                    string updated = StripOwnBlock(original) + "\n\n" + BundleMarker + "\n" + pem + "\n";

                    if (string.Equals(original.Replace("\r\n", "\n"), updated, StringComparison.Ordinal))
                        continue;

                    WriteBundle(bundle, updated);
                    patched++;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not patch trust bundle '{bundle}': {ex.Message}");
                }
            }

            if (patched > 0)
                App.Logger.WriteLine(LOG_IDENT, $"Added the proxy CA to {patched} Roblox trust bundle(s)");

            return patched;
        }

        public static int UnpatchRobloxTrustBundles()
        {
            int restored = 0;

            foreach (string bundle in FindTrustBundles())
            {
                try
                {
                    string original = File.ReadAllText(bundle);
                    if (!original.Contains(BundleMarker, StringComparison.Ordinal))
                        continue;

                    WriteBundle(bundle, StripOwnBlock(original) + "\n");
                    restored++;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not restore trust bundle '{bundle}': {ex.Message}");
                }
            }

            if (restored > 0)
                App.Logger.WriteLine(LOG_IDENT, $"Removed the proxy CA from {restored} Roblox trust bundle(s)");

            return restored;
        }

        public static bool IsRobloxTrustBundlePatched()
        {
            try
            {
                X509Certificate2? root = LoadRoot();
                if (root is null)
                    return false;

                string pem = RootPem(root);
                bool any = false;

                foreach (string bundle in BundlesThatMatter())
                {
                    any = true;

                    DateTime writeTime = File.GetLastWriteTimeUtc(bundle);
                    bool patched;

                    lock (Sync)
                    {
                        if (BundleStateCache.TryGetValue(bundle, out var cached) && cached.WriteTimeUtc == writeTime)
                        {
                            patched = cached.Patched;
                        }
                        else
                        {
                            patched = File.ReadAllText(bundle).Replace("\r\n", "\n").Contains(pem, StringComparison.Ordinal);
                            BundleStateCache[bundle] = (writeTime, patched);
                        }
                    }

                    if (!patched)
                        return false;
                }

                return any;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool? RunningRobloxTrustsProxy()
        {
            try
            {
                X509Certificate2? root = LoadRoot();
                var running = Utility.ProcessImage.RunningRoblox();
                if (running.Count == 0)
                    return null;
                if (root is null)
                    return false;

                string pem = RootPem(root);

                foreach (var (folder, started) in running)
                {
                    string bundle = Path.Combine(folder, "ssl", "cacert.pem");
                    if (!File.Exists(bundle))
                        return false;

                    if (!File.ReadAllText(bundle).Replace("\r\n", "\n").Contains(pem, StringComparison.Ordinal))
                        return false;

                    if (File.GetLastWriteTimeUtc(bundle) > started)
                        return false;
                }

                return true;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteBundle(string path, string content)
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool readOnly = attributes.HasFlag(FileAttributes.ReadOnly);
            if (readOnly)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

            try
            {
                string temporary = path + ".phasmastrap.tmp";
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (readOnly && File.Exists(path))
                    File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            }
        }
    }
}
