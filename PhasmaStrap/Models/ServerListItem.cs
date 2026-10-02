namespace PhasmaStrap.Models
{
    public sealed class ServerListItem
    {
        public string JobId { get; init; } = "";
        public int Playing { get; init; }
        public int MaxPlayers { get; init; }
        public int Ping { get; init; } = -1;

        public string PlayersText => MaxPlayers > 0 ? $"{Playing}/{MaxPlayers}" : Playing.ToString();
        public string PingText => Ping > 0 ? $"{Ping} ms" : "?";

        /// <summary>The server's own frame rate from Roblox's list, 0 when not given.</summary>
        public double Fps { get; init; }

        /// <summary>Datacenter as "City, Country" when known (from your history or PhasmaStrap's server), otherwise empty.</summary>
        public string Region { get; init; } = "";

        /// <summary>When the server was first seen, used for its uptime. Null when unknown.</summary>
        public DateTime? FirstSeenUtc { get; init; }

        public string City => PhasmaStrap.Utility.ServerFacts.SplitRegion(Region).City;

        public string ShortJobId => PhasmaStrap.Utility.ServerFacts.ShortJobId(JobId);

        /// <summary>good, warn, bad or none for the ping colour.</summary>
        public string PingTone => PhasmaStrap.Utility.ServerFacts.PingTone(Ping);

        public string FpsText => Fps > 0 ? Math.Round(Fps).ToString() : "";

        public string UptimeText => PhasmaStrap.Utility.ServerFacts.Uptime(FirstSeenUtc);

        public bool IsFull => MaxPlayers > 0 && Playing >= MaxPlayers;

        /// <summary>A copy with the facts that arrive after the list itself.</summary>
        public ServerListItem WithFacts(string region, DateTime? firstSeenUtc, double fps) => new()
        {
            JobId = JobId,
            Playing = Playing,
            MaxPlayers = MaxPlayers,
            Ping = Ping,
            Fps = Fps > 0 ? Fps : fps,
            Region = region.Length > 0 ? region : Region,
            FirstSeenUtc = firstSeenUtc ?? FirstSeenUtc,
        };
    }
}
