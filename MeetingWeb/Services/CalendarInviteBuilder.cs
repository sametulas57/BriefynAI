using System.Globalization;
using System.Text;

namespace MeetingWeb.Services
{
    public enum CalendarMethod { Request, Cancel }

    public sealed record CalendarInviteData(
        string Uid,
        int Sequence,
        DateOnly DueDate,
        string TaskText,
        string Description,
        string? Url,
        string OrganizerName,
        string OrganizerEmail,
        string AttendeeName,
        string AttendeeEmail,
        DateTime StampUtc);

    public static class CalendarInviteBuilder
    {
        public static string Build(CalendarInviteData invite, CalendarMethod method)
        {
            var cancel = method == CalendarMethod.Cancel;
            var stamp = invite.StampUtc.Kind == DateTimeKind.Local
                ? invite.StampUtc.ToUniversalTime()
                : invite.StampUtc;
            var writer = new IcsWriter();
            writer.Line("BEGIN:VCALENDAR");
            writer.Line("VERSION:2.0");
            writer.Line("PRODID:-//Briefyn AI//Gorevler//TR");
            writer.Line("CALSCALE:GREGORIAN");
            writer.Line("METHOD:" + (cancel ? "CANCEL" : "REQUEST"));
            writer.Line("BEGIN:VEVENT");
            writer.Line("UID:" + new string(invite.Uid.Where(c => !char.IsControl(c)).ToArray()));
            writer.Line("SEQUENCE:" + invite.Sequence.ToString(CultureInfo.InvariantCulture));
            writer.Line("DTSTAMP:" + stamp.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
            writer.Line("DTSTART;VALUE=DATE:" + invite.DueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            writer.Line("DTEND;VALUE=DATE:" + invite.DueDate.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            writer.Text("SUMMARY", cancel ? "İptal edilen görev" : invite.TaskText);
            if (!cancel)
            {
                if (!string.IsNullOrWhiteSpace(invite.Description))
                    writer.Text("DESCRIPTION", invite.Description);
                if (!string.IsNullOrWhiteSpace(invite.Url))
                    writer.Line("URL:" + invite.Url.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal));
            }
            writer.Line("ORGANIZER;CN=" + QuoteName(invite.OrganizerName) + ":mailto:" + CleanEmail(invite.OrganizerEmail));
            writer.Line("ATTENDEE;CN=" + QuoteName(invite.AttendeeName)
                + ";ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:" + CleanEmail(invite.AttendeeEmail));
            writer.Line("STATUS:" + (cancel ? "CANCELLED" : "CONFIRMED"));
            writer.Line("TRANSP:TRANSPARENT");
            writer.Line("X-MICROSOFT-CDO-BUSYSTATUS:FREE");
            writer.Line("X-MICROSOFT-CDO-ALLDAYEVENT:TRUE");
            if (!cancel)
            {
                writer.Line("BEGIN:VALARM");
                writer.Line("ACTION:DISPLAY");
                writer.Text("DESCRIPTION", invite.TaskText);
                writer.Line("TRIGGER:-PT15H");
                writer.Line("END:VALARM");
            }
            writer.Line("END:VEVENT");
            writer.Line("END:VCALENDAR");
            return writer.ToString();
        }

        public static string EscapeText(string value)
        {
            var escaped = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case ';':
                        escaped.Append("\\;");
                        break;
                    case ',':
                        escaped.Append("\\,");
                        break;
                    case '\r':
                        if (index + 1 < value.Length && value[index + 1] == '\n') index++;
                        escaped.Append("\\n");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    default:
                        if (character >= ' ' && character != '\u007f') escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }

        public static string Fold(string contentLine)
        {
            var folded = new StringBuilder();
            var octets = 0;
            foreach (var rune in contentLine.EnumerateRunes())
            {
                if (octets + rune.Utf8SequenceLength > 75)
                {
                    folded.Append("\r\n ");
                    octets = 1;
                }
                folded.Append(rune.ToString());
                octets += rune.Utf8SequenceLength;
            }
            return folded.ToString();
        }

        private static string QuoteName(string value) =>
            "\"" + new string(value.Where(c => c != '"' && !char.IsControl(c)).ToArray()) + "\"";

        private static string CleanEmail(string value) =>
            new(value.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c) && c is not ('"' or ';' or ':' or ',')).ToArray());

        private sealed class IcsWriter
        {
            private readonly StringBuilder _content = new();

            public void Line(string contentLine) => _content.Append(Fold(contentLine)).Append("\r\n");

            public void Text(string name, string value) => Line(name + ":" + EscapeText(value));

            public override string ToString() => _content.ToString();
        }
    }
}
