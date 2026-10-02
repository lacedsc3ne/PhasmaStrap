namespace PhasmaStrap.Utility.Backend
{
    /// <summary>What the server knows about the signed in account for the Account and backup page.</summary>
    public sealed class AccountSummary
    {
        /// <summary>True once the account has a Roblox account linked on phasmastrap.com.</summary>
        [JsonPropertyName("roblox_linked")]
        public bool RobloxLinked { get; set; }

        [JsonPropertyName("roblox_user_id")]
        public long? RobloxUserId { get; set; }

        [JsonPropertyName("roblox_name")]
        public string? RobloxName { get; set; }

        /// <summary>When the newest settings backup was stored, UTC, ISO 8601. Null when there is none.</summary>
        [JsonPropertyName("last_backup_at")]
        public DateTime? LastBackupAt { get; set; }

        /// <summary>The PC name that sent the newest backup.</summary>
        [JsonPropertyName("last_backup_machine")]
        public string? LastBackupMachine { get; set; }

        [JsonPropertyName("last_backup_files")]
        public int LastBackupFiles { get; set; }

        /// <summary>Public profile page on phasmastrap.com, for "Profile on phasmastrap.com". Null when the account has none.</summary>
        [JsonPropertyName("profile_url")]
        public string? ProfileUrl { get; set; }
    }

    /// <summary>
    /// Settings area calls to api.phasmastrap.com (see specs/settings.md).
    /// Everything fails soft: null means the server did not answer, and the page falls back to what it knows locally.
    /// </summary>
    public static class SettingsApi
    {
        /// <summary>Roblox link state and newest backup for the signed in account. Cached for a minute.</summary>
        public static Task<AccountSummary?> GetAccountSummaryAsync(bool fresh = false) =>
            PhasmaApi.GetAsync<AccountSummary>("/v1/account/summary", cacheFor: fresh ? (TimeSpan?)null : TimeSpan.FromMinutes(1), signedInOnly: true);
    }
}
