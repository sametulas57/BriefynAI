using System.Net;
using System.Text;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MeetingWeb.Tests.Infrastructure;
using static MeetingWeb.Tests.Features.PasswordResetTests;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security;

public class PasswordResetSecurityTests
{
    [Theory]
    [InlineData("/Auth/ForgotPassword", 5, null)]
    [InlineData("/Auth/ResetPassword", 10, null)]
    [InlineData("/Auth/ForgotPassword", 2, "PasswordReset:RequestsPerIpPer15Minutes")]
    [InlineData("/Auth/ResetPassword", 2, "PasswordReset:ResetsPerIpPer15Minutes")]
    public async Task Post_limits_use_matched_page_and_leave_gets_unlimited(string page, int limit, string? setting)
    {
        using var app = new BriefynFactory { UseCookieAuthentication = true };
        if (setting != null) app.ExtraSettings[setting] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await app.InitializeAsync();
        using var client = CookieClient(app);
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/Auth/Login");
        for (var i = 0; i < 12; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page)).StatusCode);

        for (var i = 0; i < limit; i++)
        {
            var response = await PostFormAsync(client, page + (i % 2 == 0 ? "/" : ""), antiforgery,
                ("Input.Email", "unknown@example.test"), ("Input.UserId", app.Seed.AliceId),
                ("Input.Code", "bm90IGEgdG9rZW4"), ("Input.Password", NewPassword),
                ("Input.ConfirmPassword", NewPassword));
            Assert.Equal(page == "/Auth/ForgotPassword" ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
            if (page == "/Auth/ForgotPassword")
                Assert.Contains("?sent=True", response.Headers.Location!.OriginalString);
        }

        var limited = await PostFormAsync(client, page, antiforgery, ("Input.Email", "unknown@example.test"));
        Assert.Equal(HttpStatusCode.Redirect, limited.StatusCode);
        Assert.Equal("/Auth/ForgotPassword", limited.Headers.Location!.OriginalString);
        Assert.Contains("Çok fazla deneme yaptınız. Lütfen biraz sonra tekrar deneyin.",
            WebUtility.HtmlDecode(await client.GetStringAsync(limited.Headers.Location)));
        for (var i = 0; i < 12; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page + "/")).StatusCode);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(0)]
    public async Task Account_email_limit_keeps_response_identical_and_expires_after_an_hour(int configuredLimit)
    {
        var clock = new ManualClock();
        using var app = new BriefynFactory
        {
            UseCookieAuthentication = true,
            TimeProviderOverride = clock,
            ExtraSettings =
            {
                ["PasswordReset:RequestsPerIpPer15Minutes"] = "20",
                ["PasswordReset:EmailsPerAccountPerHour"] = configuredLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
        await app.InitializeAsync();
        using var client = CookieClient(app);
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");
        var limit = Math.Max(1, configuredLimit);
        for (var i = 0; i < limit + 1; i++)
        {
            var response = await PostFormAsync(client, "/Auth/ForgotPassword", antiforgery, ("Input.Email", SeedData.AliceEmail));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Auth/ForgotPassword?sent=True", response.Headers.Location!.OriginalString);
        }
        Assert.Equal(limit, app.Jobs.CountFor(nameof(IPasswordResetService.SendResetEmailAsync)));

        clock.Advance(TimeSpan.FromMinutes(61));
        var renewed = await PostFormAsync(client, "/Auth/ForgotPassword", antiforgery, ("Input.Email", SeedData.AliceEmail));
        Assert.Equal(HttpStatusCode.Redirect, renewed.StatusCode);
        Assert.Equal("/Auth/ForgotPassword?sent=True", renewed.Headers.Location!.OriginalString);
        Assert.Equal(limit + 1, app.Jobs.CountFor(nameof(IPasswordResetService.SendResetEmailAsync)));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    [Theory]
    [InlineData("unknown-user", false)]
    [InlineData("unknown-user", true)]
    [InlineData("garbage", false)]
    [InlineData("garbage", true)]
    [InlineData("empty", false)]
    [InlineData("empty", true)]
    [InlineData("another-user", false)]
    [InlineData("another-user", true)]
    [InlineData("expired", false)]
    [InlineData("expired", true)]
    public async Task Invalid_reset_link_shows_explanation_without_form(string kind, bool post)
    {
        using var app = new BriefynFactory();
        if (kind == "expired") app.ExtraSettings["PasswordReset:LinkLifetimeMinutes"] = "-1";
        await PasswordAppAsync(app);
        using var client = CookieClient(app);
        var (code, _) = await ResetLinkAsync(app);
        var userId = kind == "unknown-user" ? "missing-user" : kind == "another-user" ? app.Seed.BobId : app.Seed.AliceId;
        if (kind == "garbage") code = "!garbage!";
        if (kind == "empty") code = "";

        var response = post
            ? await PostResetAsync(client, userId, code)
            : await client.GetAsync(PasswordResetLink.ResetUrl("https://localhost", userId, code));
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu bağlantı artık geçerli değil", body);
        Assert.DoesNotContain("<form", body);
        Assert.Contains("href=\"/Auth/ForgotPassword\"", body);
        await AssertPasswordAsync(app, OldPassword);
        Assert.Empty(app.Jobs.Created);
    }

    [Fact]
    public async Task Reset_rejects_reuse_and_all_links_issued_before_the_reset()
    {
        using var app = await PasswordAppAsync();
        using var client = CookieClient(app);
        var (firstCode, firstUrl) = await ResetLinkAsync(app);
        var (secondCode, secondUrl) = await ResetLinkAsync(app);
        Assert.NotEqual(firstCode, secondCode);

        Assert.Equal(HttpStatusCode.Redirect, (await PostResetAsync(client, app.Seed.AliceId, firstCode)).StatusCode);
        foreach (var (code, url) in new[] { (firstCode, firstUrl), (secondCode, secondUrl) })
        {
            var replay = await PostResetAsync(client, app.Seed.AliceId, code, "Another-Parola-123!");
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Contains("Bu bağlantı artık geçerli değil", WebUtility.HtmlDecode(await replay.Content.ReadAsStringAsync()));
            Assert.Contains("Bu bağlantı artık geçerli değil", WebUtility.HtmlDecode(await client.GetStringAsync(url)));
            await AssertPasswordAsync(app, NewPassword);
        }
        Assert.Single(app.Jobs.Created);
    }

    [Fact]
    public async Task Reset_rejects_open_session_cookie_on_next_authorized_request()
    {
        using var app = await PasswordAppAsync();
        using var session = CookieClient(app);
        using var anonymous = CookieClient(app);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(session, OldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/History")).StatusCode);
        var (code, _) = await ResetLinkAsync(app);

        Assert.Equal(HttpStatusCode.Redirect, (await PostResetAsync(anonymous, app.Seed.AliceId, code)).StatusCode);

        var response = await session.GetAsync("/History");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith("/Auth/Login", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/History")).StatusCode);
    }

    [Fact]
    public async Task Signed_in_visitor_can_submit_reset_form()
    {
        using var app = await PasswordAppAsync();
        using var client = CookieClient(app);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, OldPassword)).StatusCode);
        var (code, url) = await ResetLinkAsync(app);
        var antiforgery = await GetAntiforgeryTokenAsync(client, url);

        var response = await PostFormAsync(client, "/Auth/ResetPassword", antiforgery,
            ("Input.UserId", app.Seed.AliceId), ("Input.Code", code),
            ("Input.Password", NewPassword), ("Input.ConfirmPassword", NewPassword));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Auth/Login", response.Headers.Location!.OriginalString);
        await AssertPasswordAsync(app, NewPassword);
    }

    [Theory]
    [InlineData("valid-get")]
    [InlineData("invalid-get")]
    [InlineData("success-post")]
    [InlineData("invalid-post")]
    [InlineData("validation-post")]
    [InlineData("password-post")]
    public async Task Reset_responses_prevent_referrer_and_cache_leaks(string outcome)
    {
        using var app = await PasswordAppAsync();
        using var client = CookieClient(app);
        var (code, url) = await ResetLinkAsync(app);
        var response = outcome switch
        {
            "valid-get" => await client.GetAsync(url),
            "invalid-get" => await client.GetAsync("/Auth/ResetPassword"),
            "invalid-post" => await PostResetAsync(client, app.Seed.AliceId, "!invalid!"),
            "validation-post" => await PostResetAsync(client, app.Seed.AliceId, code, NewPassword, "mismatch"),
            "password-post" => await PostResetAsync(client, app.Seed.AliceId, code, "abcdef"),
            _ => await PostResetAsync(client, app.Seed.AliceId, code)
        };

        Assert.Equal(outcome == "success-post" ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", "abc")]
    [InlineData("known", null)]
    [InlineData("known", "")]
    [InlineData("known", " ")]
    [InlineData("known", "!invalid!")]
    [InlineData("known", "a")]
    [InlineData("known", "bm90IGEgdG9rZW4")]
    [InlineData("missing-user", "bm90IGEgdG9rZW4")]
    public async Task Service_rejects_missing_or_malformed_link_without_mutation(string? userId, string? code)
    {
        using var app = await PasswordAppAsync();
        if (userId == "known") userId = app.Seed.AliceId;
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();

        Assert.False(await service.IsLinkValidAsync(userId, code));
        var result = await service.ResetAsync(userId, code, NewPassword);

        Assert.Equal(PasswordResetOutcome.InvalidLink, result.Outcome);
        Assert.Empty(result.Errors);
        await AssertPasswordAsync(app, OldPassword);
        Assert.Empty(app.Jobs.Created);
    }

    [Fact]
    public async Task Complete_flow_does_not_log_token_code_reset_url_or_email()
    {
        var logs = new CapturingLoggerProvider();
        var mail = new RecordingEmail();
        // Framework request-start messages include URLs at Information. Production's Serilog override sets
        // Microsoft.AspNetCore to Warning; ProductionLoggingFactory mirrors that and the EF Core filter.
        using var app = await PasswordAppAsync(new ProductionLoggingFactory
        {
            LogCapture = logs,
            EmailServiceOverride = mail,
            UseCookieAuthentication = true
        });
        using var client = CookieClient(app);
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");
        var request = await PostFormAsync(client, "/Auth/ForgotPassword", antiforgery, ("Input.Email", SeedData.AliceEmail));
        Assert.Equal(HttpStatusCode.Redirect, request.StatusCode);
        var job = Assert.Single(app.Jobs.Created);
        using (var scope = app.Services.CreateScope())
            await (Task)job.Method.Invoke(scope.ServiceProvider.GetRequiredService<IPasswordResetService>(), job.Args.ToArray())!;
        var sent = Assert.Single(mail.Sent);
        var code = QueryHelpers.ParseQuery(new Uri(sent.Url).Query)["code"].ToString();
        var rawToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        Assert.NotEmpty(code);
        Assert.NotEmpty(rawToken);

        var get = await client.GetAsync(sent.Url);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Contains("Şifremi Kaydet", WebUtility.HtmlDecode(await get.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await PostResetAsync(client, app.Seed.AliceId, code)).StatusCode);
        await AssertPasswordAsync(app, NewPassword);
        var changedJob = Assert.Single(app.Jobs.Created, j => j.Method.Name == nameof(IPasswordResetService.SendPasswordChangedEmailAsync));
        using (var scope = app.Services.CreateScope())
            await (Task)changedJob.Method.Invoke(scope.ServiceProvider.GetRequiredService<IPasswordResetService>(), changedJob.Args.ToArray())!;
        Assert.Single(mail.Changed);

        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PasswordResetSecurityTests").LogInformation("Password reset flow completed.");
        Assert.Contains(logs.Lines, line => line.Contains("Password reset flow completed.", StringComparison.Ordinal));
        Assert.All(logs.Lines, line =>
        {
            Assert.DoesNotContain(rawToken, line);
            Assert.DoesNotContain(code, line);
            Assert.DoesNotContain(sent.Url, line);
            Assert.DoesNotContain(SeedData.AliceEmail, line);
            Assert.DoesNotContain(NewPassword, line);
        });
    }
}
