using System.Net;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features
{
    public class UserFacingFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Follow-ups from Ulaş's browser check after S4: times were shown in UTC, report emails started landing in spam,
    // and the share confirmation talked about "background" processing.
    public class UserFacingTests : IClassFixture<UserFacingFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public UserFacingTests(UserFacingFixture fixture) => _app = fixture.Factory;

        [Fact]
        public void Utc_times_are_shown_in_turkish_time_and_format()
        {
            var utc = new DateTime(2026, 10, 17, 21, 30, 0, DateTimeKind.Unspecified); // EF returns Unspecified

            Assert.Equal("18 Ekim 2026, 00:30", DisplayTime.Format(utc, "dd MMMM yyyy, HH:mm"));
        }

        // Razor HTML-encodes non-ASCII letters (ç → &#xE7;); browsers show them normally, so compare decoded text.
        private static async Task<string> PageTextAsync(HttpClient client, string url) =>
            WebUtility.HtmlDecode(await client.GetStringAsync(url));

        [Fact]
        public async Task Meeting_page_shows_turkish_time()
        {
            // The date badge is rendered for analysed meetings.
            var created = new DateTime(2026, 10, 17, 21, 30, 0, DateTimeKind.Utc);
            var id = _app.WithDb(db =>
            {
                var m = new Meeting { WorkspaceId = S.WorkspaceA, Title = "Saat testi", Status = MeetingStatus.Completed, CreatedAtUtc = created };
                db.Meetings.Add(m);
                db.SaveChanges();
                return m.Id;
            });

            var page = await PageTextAsync(_app.ClientFor(S.AliceId), $"/Details?id={id}");

            Assert.Contains("18 Ekim 2026, 00:30", page);
        }

        [Fact]
        public void Email_keeps_turkish_letters_readable_and_still_blocks_html()
        {
            var meeting = new Meeting
            {
                Title = "Lansman öncesi değerlendirme",
                Points = new List<MeetingPoint>
                {
                    new() { Kind = MeetingPointKind.Topic, Text = "Tedarikçi değişti" },
                    new() { Kind = MeetingPointKind.Topic, Text = "<script>x</script>" }
                }
            };

            var html = EmailService.BuildSummaryHtml(meeting);

            Assert.Contains("Tedarikçi değişti", html);       // no &#231; style entities, which spam filters dislike
            Assert.Contains("Lansman öncesi değerlendirme", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void Email_has_a_plain_text_version()
        {
            var meeting = new Meeting
            {
                Title = "Sprint",
                Points = new List<MeetingPoint> { new() { Kind = MeetingPointKind.Decision, Text = "Release salıya kaldı", Source = EntrySource.Manual, CreatedByUserId = "user-1" } },
                ActionItems = new List<ActionItem> { new() { Text = "Deniz: hotfix" } }
            };

            var text = EmailService.BuildSummaryText(meeting);

            Assert.Contains("Release salıya kaldı", text);
            Assert.Contains("Deniz: hotfix", text);
            Assert.DoesNotContain("<", text);
            Assert.DoesNotContain("user-1", text);
        }

        [Fact]
        public async Task Share_confirmation_is_plain_language()
        {
            var alice = _app.ClientFor(S.AliceId);
            var token = await GetAntiforgeryTokenAsync(alice, $"/Details?id={S.MeetingA}");

            var res = await PostFormAsync(alice, $"/Details?id={S.MeetingA}&handler=Share", token,
                ("meetingId", $"{S.MeetingA}"), ("selectedEmails", SeedData.CarolEmail));
            var page = await PageTextAsync(alice, res.Headers.Location!.ToString());

            Assert.DoesNotContain("arka plan", page, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("e-postayla gönderiliyor", page);
        }

        [Fact]
        public async Task Hitting_the_share_limit_explains_what_happened()
        {
            var bob = _app.ClientFor(S.BobId);
            var url = $"/Details?id={S.MeetingB}";
            var token = await GetAntiforgeryTokenAsync(bob, url);
            bob.DefaultRequestHeaders.Referrer = new Uri("https://localhost" + url);

            HttpResponseMessage last = null!;
            for (var i = 0; i < 11; i++)
                last = await PostFormAsync(bob, url + "&handler=Share", token, ("meetingId", $"{S.MeetingB}"), ("selectedEmails", SeedData.BobEmail));

            Assert.Equal(HttpStatusCode.Redirect, last.StatusCode);
            var page = await PageTextAsync(bob, last.Headers.Location!.ToString());
            Assert.Contains("çok fazla rapor", page);
        }

        // The cookie's "access denied" path pointed at a page that did not exist.
        [Fact]
        public async Task Access_denied_goes_to_an_existing_page_with_a_clear_message()
        {
            var options = _app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
                .Get(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
            var page = await PageTextAsync(_app.ClientFor(S.AliceId), options.AccessDeniedPath.Value!);

            Assert.Contains("erişim izniniz yok", page);
        }

        // Members without a name used to be called "İsimsiz", "İsimsiz Üye" or "İsimsiz Kullanıcı" depending on the page.
        [Fact]
        public async Task A_member_without_a_name_is_called_the_same_everywhere()
        {
            var members = await PageTextAsync(_app.ClientFor(S.AliceId), "/Workspace/Members");

            Assert.Contains(UserDisplay.UnknownName, members);
            Assert.DoesNotContain("İsimsiz Kullanıcı", members);
        }

        // The privacy text still described the old offline models; recordings now go to Groq with zero data retention.
        [Fact]
        public async Task Privacy_text_describes_the_current_ai_provider()
        {
            var page = await PageTextAsync(_app.ClientFor(S.AliceId), "/Privacy");

            Assert.Contains("Groq", page);
            Assert.DoesNotContain("offline", page);
            Assert.DoesNotContain("yerel sunucularımızda", page);
        }
    }
}
