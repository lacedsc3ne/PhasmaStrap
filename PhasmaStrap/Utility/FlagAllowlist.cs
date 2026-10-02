namespace PhasmaStrap.Utility
{
    public static class FlagAllowlist
    {
        public const string Source = "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";

        public const string AsOf = "29 September 2025";

        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",
            "FFlagHandleAltEnterFullscreenManually",
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntDebugForceMSAASamples",
            "DFFlagDisableDPIScale",
            "FFlagDebugGraphicsPreferD3D11",
            "FFlagDebugSkyGray",
            "DFFlagDebugPauseVoxelizer",
            "DFIntDebugFRMQualityLevelOverride",
            "FIntFRMMaxGrassDistance",
            "FIntFRMMinGrassDistance",
            "FFlagDebugGraphicsPreferVulkan",
            "FFlagDebugGraphicsPreferOpenGL",
            "FIntGrassMovementReducedMotionFactor",
        };

        public static IReadOnlyCollection<string> All => Allowed;

        public static bool Allows(string flagName) => Allowed.Contains(flagName.Trim());

        public static List<string> Ignored(IEnumerable<string> flagNames) =>
            flagNames.Where(name => !Allows(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
