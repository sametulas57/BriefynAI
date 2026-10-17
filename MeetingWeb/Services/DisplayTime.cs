using System.Globalization;

namespace MeetingWeb.Services
{
    // All timestamps are stored in UTC; users see them in Turkish time and Turkish formatting.
    // (Before this, meeting dates were printed in UTC, three hours behind, and comments used a hard-coded +3.)
    public static class DisplayTime
    {
        public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");
        private static readonly TimeZoneInfo Zone = FindTurkeyZone();

        public static string Format(DateTime utc, string format) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString(format, Culture);

        private static TimeZoneInfo FindTurkeyZone()
        {
            foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
            }
            // Containers without tzdata: Turkey has used a fixed UTC+3 without daylight saving since 2016.
            return TimeZoneInfo.CreateCustomTimeZone("Turkey", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
        }
    }
}
