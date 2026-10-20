using MeetingWeb.Services;
using System.Net;
using MeetingWeb.Tests.Infrastructure;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class ExtraRecipientFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // Ulaş: besides team members, the share dialog gets a line for extra addresses (someone outside the team,
    // or a summary from a personal space). Capped per send so the sender account cannot become a spam relay.
    public class ExtraRecipientTests : IClassFixture<ExtraRecipientFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public ExtraRecipientTests(ExtraRecipientFixture fixture) => _app = fixture.Factory;

        private async Task<string> ShareAsync(string extraEmails)
        {
            var alice = _app.ClientFor(S.AliceId);
            var url = $"/Details?id={S.MeetingA}";
            var token = await GetAntiforgeryTokenAsync(alice, url);
            var res = await PostFormAsync(alice, url + "&handler=Share", token,
                ("meetingId", $"{S.MeetingA}"), ("extraEmails", extraEmails));
            return WebUtility.HtmlDecode(await alice.GetStringAsync(res.Headers.Location!.ToString()));
        }

        private int SentTo(string email) => _app.Jobs.CountFor("SendMeetingSummaryAsync", a => EmailService.UnprotectRecipient((string)a[1]!) == email);

        [Fact]
        public void Share_dialog_has_an_extra_address_line()
        {
            var page = WebUtility.HtmlDecode(_app.ClientFor(S.AliceId).GetStringAsync($"/Details?id={S.MeetingA}").Result);
            Assert.Contains("name=\"extraEmails\"", page);
            Assert.Contains("Ekip dışından birine de göndermek", page);
        }

        [Fact]
        public async Task Extra_addresses_receive_the_report()
        {
            var page = await ShareAsync("disari@musteri.test, ikinci@musteri.test");

            Assert.Equal(1, SentTo("disari@musteri.test"));
            Assert.Equal(1, SentTo("ikinci@musteri.test"));
            Assert.Contains("e-postayla gönderiliyor", page);
        }

        [Fact]
        public async Task An_invalid_address_sends_nothing_and_says_which_one()
        {
            var page = await ShareAsync("gecerli@musteri.test, bu-bir-adres-degil");

            Assert.Equal(0, SentTo("gecerli@musteri.test"));
            Assert.Contains("bu-bir-adres-degil", page);
            Assert.Contains("geçerli bir e-posta adresi değil", page);
        }

        [Fact]
        public async Task More_than_three_extra_addresses_are_refused()
        {
            var page = await ShareAsync("a@x.test, b@x.test, c@x.test, d@x.test");

            Assert.Equal(0, SentTo("a@x.test"));
            Assert.Contains("en fazla 3", page);
        }
    }
}
