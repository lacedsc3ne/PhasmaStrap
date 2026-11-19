namespace PhasmaStrap.Utility.Backend
{
    public sealed class FriendSince
    {
        [JsonPropertyName("roblox_user_id")]
        public string RobloxUserId { get; set; } = "";

        [JsonPropertyName("since")]
        public DateTime Since { get; set; }
    }

    public sealed class FriendsSinceResponse
    {
        [JsonPropertyName("friends")]
        public List<FriendSince> Friends { get; set; } = new();
    }

    public static class SocialApi
    {
        private const string LOG_IDENT = "SocialApi";

        public static async Task<Dictionary<long, DateTime>> GetFriendsSinceAsync(long accountId, IEnumerable<long> friendIds)
        {
            var result = new Dictionary<long, DateTime>();
            List<long> ids = friendIds.Where(id => id > 0).Distinct().Take(200).ToList();

            if (accountId <= 0 || ids.Count == 0)
                return result;

            FriendsSinceResponse? response = await PhasmaApi.GetAsync<FriendsSinceResponse>(
                $"/v1/social/friends/since?roblox_user_id={accountId}&friend_ids={string.Join(",", ids)}",
                cacheFor: TimeSpan.FromHours(6));

            if (response?.Friends is null)
                return result;

            foreach (FriendSince item in response.Friends)
            {
                if (long.TryParse(item.RobloxUserId, out long id) && item.Since > DateTime.MinValue)
                    result[id] = DateTime.SpecifyKind(item.Since, DateTimeKind.Utc);
            }

            return result;
        }

        public static async Task ReportNewFriendsAsync(long accountId, IReadOnlyCollection<(long UserId, DateTime FirstSeenUtc)> added)
        {
            if (accountId <= 0 || added.Count == 0)
                return;

            var body = new
            {
                roblox_user_id = accountId.ToString(),
                friends = added.Select(a => new { roblox_user_id = a.UserId.ToString(), first_seen = a.FirstSeenUtc.ToString("o") }).ToList(),
            };

            if (!await PhasmaApi.PostAsync("/v1/social/friends/seen", body))
                App.Logger.WriteLine(LOG_IDENT, $"The server did not take {added.Count} new friend(s)");
        }
    }
}
