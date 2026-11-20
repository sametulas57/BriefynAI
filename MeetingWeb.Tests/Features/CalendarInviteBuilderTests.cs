using System.Globalization;
using System.Text;
using MeetingWeb.Services;

namespace MeetingWeb.Tests.Features;

public class CalendarInviteBuilderTests
{
    [Fact]
    public void Publish_keeps_request_content_and_alarm_without_attendee()
    {
        var invite = CreateInvite();
        var output = Unfold(CalendarInviteBuilder.Build(invite, CalendarMethod.Publish));
        var request = Unfold(CalendarInviteBuilder.Build(invite, CalendarMethod.Request));

        Assert.Contains("METHOD:PUBLISH\r\n", output);
        Assert.DoesNotContain(output.Split("\r\n"), line => line.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.Contains("SUMMARY:" + invite.TaskText + "\r\n", output);
        Assert.Contains("BEGIN:VALARM\r\n", output);
        Assert.Equal(request.Split("\r\n").Where(line => !line.StartsWith("ATTENDEE", StringComparison.Ordinal))
            .Select(line => line == "METHOD:REQUEST" ? "METHOD:PUBLISH" : line), output.Split("\r\n"));
    }

    [Fact]
    public void Request_contains_ordered_all_day_event_and_alarm()
    {
        var invite = CreateInvite();

        var output = CalendarInviteBuilder.Build(invite, CalendarMethod.Request);

        Assert.Equal(new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Briefyn AI//Gorevler//TR",
            "CALSCALE:GREGORIAN", "METHOD:REQUEST", "BEGIN:VEVENT",
            "UID:task-42@briefyn", "SEQUENCE:3", "DTSTAMP:20261120T123456Z",
            "DTSTART;VALUE=DATE:20261128", "DTEND;VALUE=DATE:20261129",
            "SUMMARY:Prepare report", "DESCRIPTION:Meeting notes\\nSecond line",
            "URL:https://briefyn.example/MyActions",
            "ORGANIZER;CN=\"Briefyn AI\":mailto:organizer@example.com",
            "ATTENDEE;CN=\"Ayşe\";ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:ayse@example.com",
            "STATUS:CONFIRMED", "TRANSP:TRANSPARENT", "X-MICROSOFT-CDO-BUSYSTATUS:FREE",
            "X-MICROSOFT-CDO-ALLDAYEVENT:TRUE", "BEGIN:VALARM", "ACTION:DISPLAY",
            "DESCRIPTION:Prepare report", "TRIGGER:-PT15H", "END:VALARM", "END:VEVENT", "END:VCALENDAR", ""
        }, Unfold(output).Split("\r\n"));
    }

    [Theory]
    [InlineData(2026, 12, 31, "20270101")]
    [InlineData(2026, 11, 30, "20261201")]
    [InlineData(2028, 2, 29, "20280301")]
    public void End_date_rolls_into_next_month_or_year(int year, int month, int day, string expected)
    {
        var invite = CreateInvite() with { DueDate = new DateOnly(year, month, day) };

        Assert.Contains("DTEND;VALUE=DATE:" + expected + "\r\n",
            CalendarInviteBuilder.Build(invite, CalendarMethod.Request));
    }

    [Fact]
    public void Cancel_keeps_identity_and_sequence_without_disclosing_task_content()
    {
        var invite = CreateInvite() with { Sequence = 4 };

        var output = Unfold(CalendarInviteBuilder.Build(invite, CalendarMethod.Cancel));

        Assert.Contains("METHOD:CANCEL\r\n", output);
        Assert.Contains("STATUS:CANCELLED\r\n", output);
        Assert.Contains("UID:" + invite.Uid + "\r\n", output);
        Assert.Contains("SEQUENCE:4\r\n", output);
        Assert.Contains("SUMMARY:İptal edilen görev\r\n", output);
        Assert.DoesNotContain(invite.TaskText, output);
        Assert.DoesNotContain("DESCRIPTION:", output);
        Assert.DoesNotContain("Meeting notes", output);
        Assert.DoesNotContain(invite.Url!, output);
        Assert.DoesNotContain("URL:", output);
        Assert.DoesNotContain("VALARM", output);
    }

    [Theory]
    [InlineData("a;b,c\\d", "a\\;b\\,c\\\\d")]
    [InlineData("x\r\ny\nz", "x\\ny\\nz")]
    [InlineData("x\ry", "x\\ny")]
    [InlineData("a\0\t\u001f\u007fb", "ab")]
    [InlineData("", "")]
    public void Text_is_escaped_and_controls_are_removed(string value, string expected)
    {
        Assert.Equal(expected, CalendarInviteBuilder.EscapeText(value));
    }

    [Theory]
    [InlineData(CalendarMethod.Request)]
    [InlineData(CalendarMethod.Cancel)]
    public void Every_line_including_last_ends_with_crlf(CalendarMethod method)
    {
        var invite = CreateInvite() with { TaskText = "First\r\nSecond\nThird\rFourth" };

        var output = CalendarInviteBuilder.Build(invite, method);

        Assert.EndsWith("END:VCALENDAR\r\n", output);
        Assert.All(output.Split("\r\n"), line =>
        {
            Assert.DoesNotContain('\r', line);
            Assert.DoesNotContain('\n', line);
        });
    }

    [Theory]
    [InlineData("Çağrı merkezi için ğüşıöçİ ")]
    [InlineData("Çağrı 😀🚀 için ğüşıöçİ ")]
    public void Long_unicode_lines_fold_without_splitting_characters(string segment)
    {
        var text = string.Concat(Enumerable.Repeat(segment, 20));
        var invite = CreateInvite() with { TaskText = text };
        var expected = "SUMMARY:" + CalendarInviteBuilder.EscapeText(text);

        var folded = CalendarInviteBuilder.Fold(expected);
        var output = CalendarInviteBuilder.Build(invite, CalendarMethod.Request);

        Assert.Contains("\r\n ", folded);
        Assert.False(folded.EndsWith("\r\n", StringComparison.Ordinal));
        Assert.Equal(expected, Unfold(folded));
        AssertValidPhysicalLines(folded);
        AssertValidPhysicalLines(output);
        Assert.Equal(expected, Assert.Single(Unfold(output).Split("\r\n"),
            line => line.StartsWith("SUMMARY:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(75, 1)]
    [InlineData(76, 2)]
    [InlineData(149, 2)]
    [InlineData(150, 3)]
    public void Fold_respects_first_and_continuation_byte_limits(int length, int lineCount)
    {
        var input = new string('a', length);

        var folded = CalendarInviteBuilder.Fold(input);

        Assert.Equal(lineCount, folded.Split("\r\n").Length);
        Assert.All(folded.Split("\r\n").Skip(1), line => Assert.StartsWith(" ", line));
        Assert.Equal(input, Unfold(folded));
        AssertValidPhysicalLines(folded);
    }

    [Fact]
    public void Untrusted_fields_cannot_inject_attendee_or_organizer_lines()
    {
        const string injection = "\r\nATTENDEE:mailto:evil@x.com\r\nORGANIZER:mailto:evil@x.com";
        var invite = CreateInvite() with
        {
            AttendeeName = "Ayşe" + injection,
            AttendeeEmail = "ayse@example.com" + injection,
            OrganizerName = "Briefyn" + injection,
            OrganizerEmail = "organizer@example.com" + injection,
            TaskText = "Task" + injection,
            Uid = "task-42@briefyn\r\n\0\t\u007f"
        };

        var output = CalendarInviteBuilder.Build(invite, CalendarMethod.Request);
        var lines = output.Split("\r\n");

        Assert.Single(lines, line => line.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.Single(lines, line => line.StartsWith("ORGANIZER", StringComparison.Ordinal));
        Assert.Contains("UID:task-42@briefyn\r\n", output);
        Assert.Contains("SUMMARY:Task\\nATTENDEE:mailto:evil@x.com\\nORGANIZER:mailto:evil@x.com", Unfold(output));
    }

    [Fact]
    public void Names_and_emails_are_sanitized_for_parameters_and_addresses()
    {
        var invite = CreateInvite() with
        {
            OrganizerName = "Brief\"yn\r\n\t\0\u007f",
            AttendeeName = "Ay\"şe\r\n\t\0\u007f",
            OrganizerEmail = " org\r\n\t\";:,anizer@example.com ",
            AttendeeEmail = " ay\r\n\t\";:,se@example.com "
        };

        var output = Unfold(CalendarInviteBuilder.Build(invite, CalendarMethod.Request));

        Assert.Contains("ORGANIZER;CN=\"Briefyn\":mailto:organizer@example.com\r\n", output);
        Assert.Contains("ATTENDEE;CN=\"Ayşe\";ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:ayse@example.com\r\n", output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_url_and_description_are_omitted(string? value)
    {
        var invite = CreateInvite() with { Url = value, Description = value ?? "" };

        var output = Unfold(CalendarInviteBuilder.Build(invite, CalendarMethod.Request));

        Assert.DoesNotContain("\r\nURL:", output);
        Assert.DoesNotContain("\r\nDESCRIPTION:\r\n", output);
        Assert.DoesNotContain("\r\nDESCRIPTION:   \r\n", output);
    }

    [Fact]
    public void Url_strips_line_breaks_without_text_escaping()
    {
        var invite = CreateInvite() with { Url = "https://briefyn.example/tasks?a=1,2;3\r\n&b=4" };

        var output = Unfold(CalendarInviteBuilder.Build(invite, CalendarMethod.Request));

        Assert.Contains("\r\nURL:https://briefyn.example/tasks?a=1,2;3&b=4\r\n", output);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Dates_and_sequence_are_invariant_and_stamp_is_utc(DateTimeKind kind)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var stamp = new DateTime(2026, 11, 20, 12, 34, 56, kind);
            var invite = CreateInvite() with { StampUtc = stamp };
            var expectedStamp = kind == DateTimeKind.Local ? stamp.ToUniversalTime() : stamp;

            var output = CalendarInviteBuilder.Build(invite, CalendarMethod.Request);

            Assert.Contains("DTSTAMP:" + expectedStamp.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "\r\n", output);
            Assert.Contains("DTSTART;VALUE=DATE:20261128\r\n", output);
            Assert.Contains("DTEND;VALUE=DATE:20261129\r\n", output);
            Assert.Contains("SEQUENCE:3\r\n", output);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static CalendarInviteData CreateInvite() => new(
        "task-42@briefyn", 3, new DateOnly(2026, 11, 28), "Prepare report",
        "Meeting notes\r\nSecond line", "https://briefyn.example/MyActions",
        "Briefyn AI", "organizer@example.com", "Ayşe", "ayse@example.com",
        new DateTime(2026, 11, 20, 12, 34, 56, DateTimeKind.Utc));

    private static string Unfold(string value) => value.Replace("\r\n ", "", StringComparison.Ordinal);

    private static void AssertValidPhysicalLines(string value)
    {
        var utf8 = new UTF8Encoding(false, true);
        Assert.All(value.Split("\r\n"), line =>
        {
            var bytes = utf8.GetBytes(line);
            Assert.InRange(bytes.Length, 0, 75);
            Assert.Equal(line, utf8.GetString(bytes));
            Assert.DoesNotContain('\uFFFD', line);
        });
    }
}
