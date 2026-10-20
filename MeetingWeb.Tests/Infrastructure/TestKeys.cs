namespace MeetingWeb.Tests.Infrastructure
{
    // Fixed test encryption key (32 bytes, Base64), set before the app starts.
    public static class TestKeys
    {
        public static readonly string KeyV2Base64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    }
}
