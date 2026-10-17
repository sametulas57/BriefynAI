using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class EmailFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S8: meeting content comes from the LLM and from users, so it must be encoded before it goes into an HTML email,
    // and sharing must be rate limited so the sender account cannot be turned into a spam relay.
    public class EmailTests : IClassFixture<EmailFixture>
    {
        private readonly BriefynFactory _app;

        public EmailTests(EmailFixture fixture) => _app = fixture.Factory;

        private static MeetingSummary Hostile() => new()
        {
            ToplantiKonusu = "<script>alert('konu')</script>",
            GorusulenKonular = new List<string> { "<img src=x onerror=alert(1)>|~|user-1" },
            AlinanKararlar = new List<string> { "<a href=\"https://evil.test\">tıkla</a>" },
            AksiyonMaddeleri = new List<ActionItem> { new() { Text = "<b>görev</b>|~|user-2" } }
        };

        [Fact]
        public void Summary_email_encodes_meeting_content()
        {
            var html = EmailService.BuildSummaryHtml(Hostile());

            Assert.DoesNotContain("<script>", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("href=\"https://evil.test\"", html);
            Assert.DoesNotContain("<b>görev</b>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain("user-1", html); // author ids stay out of the email
        }

        [Fact]
        public void Subject_cannot_carry_extra_header_lines()
        {
            var subject = EmailService.BuildSubject(new MeetingSummary { ToplantiKonusu = "Konu\r\nBcc: victim@evil.test" });

            Assert.DoesNotContain("\r", subject);
            Assert.DoesNotContain("\n", subject);
        }

        [Fact]
        public async Task Sharing_is_rate_limited_per_user()
        {
            var s = _app.Seed;
            var alice = _app.ClientFor(s.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={s.MeetingA}");

            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 11; i++)
            {
                var res = await PostFormAsync(alice, $"/Details?id={s.MeetingA}&handler=Share", token,
                    ("meetingId", $"{s.MeetingA}"), ("selectedEmails", SeedData.CarolEmail));
                statuses.Add(res.StatusCode);
            }

            // Over the limit the user is sent back with an explanation (UserFacingTests checks the message).
            Assert.Equal(HttpStatusCode.Redirect, statuses[^1]);
            Assert.True(_app.Jobs.CountFor("SendMeetingSummaryAsync") <= 10);
        }
    }
}
