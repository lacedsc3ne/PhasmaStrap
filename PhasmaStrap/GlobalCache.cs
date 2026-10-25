namespace PhasmaStrap
{
    public static class GlobalCache
    {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> ServerLocation = new();
    }
}
