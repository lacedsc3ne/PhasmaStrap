namespace PhasmaStrap.Utility.Backend
{
    public sealed class AccountSummary
    {
        [JsonPropertyName("roblox_linked")]
        public bool RobloxLinked { get; set; }

        [JsonPropertyName("roblox_user_id")]
        public long? RobloxUserId { get; set; }

        [JsonPropertyName("roblox_name")]
        public string? RobloxName { get; set; }

        [JsonPropertyName("last_backup_at")]
        public DateTime? LastBackupAt { get; set; }

        [JsonPropertyName("last_backup_machine")]
        public string? LastBackupMachine { get; set; }

        [JsonPropertyName("last_backup_files")]
        public int LastBackupFiles { get; set; }

        [JsonPropertyName("profile_url")]
        public string? ProfileUrl { get; set; }
    }

    public static class SettingsApi
    {
        public static Task<AccountSummary?> GetAccountSummaryAsync(bool fresh = false) =>
            PhasmaApi.GetAsync<AccountSummary>("/v1/account/summary", cacheFor: fresh ? (TimeSpan?)null : TimeSpan.FromMinutes(1), signedInOnly: true);
    }
}
