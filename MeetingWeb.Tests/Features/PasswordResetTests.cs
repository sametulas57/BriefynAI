using System.Net;
using System.Text;
using Hangfire.States;
using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Features;

public class PasswordResetTests
{
    private static HttpClient AnonymousClient(BriefynFactory app, string host = "localhost") =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://" + host),
            AllowAutoRedirect = false
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_reset_link_shows_form_and_hidden_inputs_even_when_signed_in(bool signedIn)
    {
        using var app = await ReadyAsync();
        using var client = signedIn ? app.ClientFor(app.Seed.BobId) : AnonymousClient(app);
        var (code, url) = await ResetLinkAsync(app);

        var response = await client.GetAsync(url);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Şifremi Kaydet", body);
        Assert.Contains($"name=\"Input.UserId\" value=\"{app.Seed.AliceId}\"", body);
        Assert.Contains($"name=\"Input.Code\" value=\"{code}\"", body);
    }

    internal const string OldPassword = "Eski-Parola-123!";
    internal const string NewPassword = "Yeni-Parola-123!";

    [Fact]
    public async Task Reset_redirects_with_confirmation_and_only_new_password_can_sign_in()
    {
        using var app = await PasswordAppAsync();
        using var client = CookieClient(app);
        var (code, _) = await ResetLinkAsync(app);

        var reset = await PostResetAsync(client, app.Seed.AliceId, code);

        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/Auth/Login", reset.Headers.Location!.OriginalString);
        Assert.Contains("Şifreniz değişti. Yeni şifrenizle giriş yapın.",
            WebUtility.HtmlDecode(await client.GetStringAsync(reset.Headers.Location)));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, OldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/History")).StatusCode);
    }

    [Theory]
    [InlineData("abcdef", "abcdef", "Passwords must have")]
    [InlineData(NewPassword, "Different-Password-123!", "Şifreler birbiriyle eşleşmiyor")]
    [InlineData("", "", "Şifre alanı zorunludur.")]
    [InlineData("abc", "abc", "Şifreniz en az 6 karakter uzunluğunda olmalıdır.")]
    public async Task Rejected_password_or_confirmation_preserves_password_and_valid_link(string password, string confirm, string error)
    {
        using var app = await PasswordAppAsync();
        using var client = CookieClient(app);
        var (code, url) = await ResetLinkAsync(app);

        var response = await PostResetAsync(client, app.Seed.AliceId, code, password, confirm);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(error, body);
        Assert.Contains("Şifremi Kaydet", body);
        await AssertPasswordAsync(app, OldPassword);
        using var scope = app.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IPasswordResetService>().IsLinkValidAsync(app.Seed.AliceId, code));
        Assert.Contains("Şifremi Kaydet", WebUtility.HtmlDecode(await client.GetStringAsync(url)));
        Assert.Empty(app.Jobs.Created);
    }

    [Fact]
    public async Task Reset_unlocks_account_and_clears_failed_attempts_before_new_login()
    {
        using var app = await PasswordAppAsync();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(app.Seed.AliceId))!;
            user.AccessFailedCount = 3;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True((await users.SetLockoutEnabledAsync(user, true)).Succeeded);
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(1))).Succeeded);
        }
        using var client = CookieClient(app);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, OldPassword)).StatusCode);
        var (code, _) = await ResetLinkAsync(app);

        Assert.Equal(HttpStatusCode.Redirect, (await PostResetAsync(client, app.Seed.AliceId, code)).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(app.Seed.AliceId))!;
            Assert.False(await users.IsLockedOutAsync(user));
            Assert.Null(user.LockoutEnd);
            Assert.Equal(0, user.AccessFailedCount);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/History")).StatusCode);
    }

    [Fact]
    public async Task Successful_reset_queues_one_notification_with_only_user_id_and_job_sends_email()
    {
        var mail = new RecordingEmail();
        using var app = await PasswordAppAsync(new BriefynFactory { EmailServiceOverride = mail, UseCookieAuthentication = true });
        app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.AliceId).FullName = "Alice Example"; return db.SaveChanges(); });
        using var client = CookieClient(app);
        var (code, _) = await ResetLinkAsync(app);

        Assert.Equal(HttpStatusCode.Redirect, (await PostResetAsync(client, app.Seed.AliceId, code)).StatusCode);

        var job = Assert.Single(app.Jobs.Created);
        Assert.Equal("SendPasswordChangedEmailAsync", job.Method.Name);
        Assert.Equal(typeof(IPasswordResetService), job.Type);
        Assert.Equal("eposta", job.Queue);
        Assert.IsType<EnqueuedState>(app.Jobs.States[job]);
        Assert.Equal(new object[] { app.Seed.AliceId }, job.Args);
        Assert.Empty(mail.Changed);
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        await (Task)job.Method.Invoke(service, job.Args.ToArray())!;
        Assert.Equal((SeedData.AliceEmail, "Alice Example"), Assert.Single(mail.Changed));
    }

    [Fact]
    public async Task Notification_enqueue_failure_logs_event_but_reset_still_succeeds()
    {
        var logs = new CapturingLoggerProvider();
        using var app = await PasswordAppAsync(new ProductionLoggingFactory { LogCapture = logs, UseCookieAuthentication = true });
        using var client = CookieClient(app);
        var (code, _) = await ResetLinkAsync(app);
        app.Jobs.FailNext = true;

        var response = await PostResetAsync(client, app.Seed.AliceId, code);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await AssertPasswordAsync(app, NewPassword);
        Assert.Empty(app.Jobs.Created);
        var log = string.Join("\n", logs.Lines);
        Assert.Contains("Password changed email could not be queued.", log);
        Assert.DoesNotContain(SeedData.AliceEmail, log);
        Assert.DoesNotContain(code, log);
    }

    internal static async Task<BriefynFactory> PasswordAppAsync(BriefynFactory? app = null)
    {
        app ??= new BriefynFactory { UseCookieAuthentication = true };
        await app.InitializeAsync();
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.AddPasswordAsync((await users.FindByIdAsync(app.Seed.AliceId))!, OldPassword)).Succeeded);
        return app;
    }

    internal static HttpClient CookieClient(BriefynFactory app) => app.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    internal static async Task<HttpResponseMessage> PostResetAsync(HttpClient client, string userId, string code,
        string password = NewPassword, string? confirm = null)
    {
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/Auth/Login");
        return await PostFormAsync(client, "/Auth/ResetPassword", antiforgery,
            ("Input.UserId", userId), ("Input.Code", code),
            ("Input.Password", password), ("Input.ConfirmPassword", confirm ?? password));
    }

    internal static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
    {
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/Auth/Login");
        return await PostFormAsync(client, "/Auth/Login", antiforgery,
            ("Input.Email", SeedData.AliceEmail), ("Input.Password", password));
    }

    internal static async Task AssertPasswordAsync(BriefynFactory app, string password)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(app.Seed.AliceId))!, password));
    }

    internal static async Task<(string Code, string Url)> ResetLinkAsync(BriefynFactory app)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(app.Seed.AliceId))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return (code, PasswordResetLink.ResetUrl("https://localhost", user.Id, code));
    }

    [Fact]
    public async Task Known_and_unknown_email_receive_identical_response_and_only_known_email_enqueues()
    {
        using var app = await ReadyAsync();
        using var client = AnonymousClient(app);
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");

        var known = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", "  " + SeedData.AliceEmail + "  "));
        Assert.Equal(1, app.Jobs.CountFor(nameof(IPasswordResetService.SendResetEmailAsync)));
        var unknown = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", "unknown@example.test"));

        Assert.Equal(HttpStatusCode.Redirect, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        Assert.Equal("/Auth/ForgotPassword?sent=True", known.Headers.Location!.OriginalString);
        var confirmation = WebUtility.HtmlDecode(await client.GetStringAsync("/Auth/ForgotPassword?sent=true"));
        Assert.Contains("Bu adrese kayıtlı bir hesap varsa", confirmation);
        Assert.Contains("bağlantı 1 saat geçerli", confirmation);
        Assert.Equal(1, app.Jobs.CountFor(nameof(IPasswordResetService.SendResetEmailAsync)));
        var job = Assert.Single(app.Jobs.Created);
        Assert.Equal(typeof(IPasswordResetService), job.Type);
        Assert.IsType<EnqueuedState>(app.Jobs.States[job]);
        // Hangfire 1.8 stores the queue on Job; the fake captures state before Hangfire applies it.
        Assert.Equal("eposta", job.Queue);
        Assert.Equal(new object[] { app.Seed.AliceId, "http://localhost" }, job.Args);
        Assert.All(job.Args, arg => Assert.DoesNotContain(SeedData.AliceEmail, Assert.IsType<string>(arg)));
    }

    [Fact]
    public async Task Queued_job_sends_valid_token_generated_at_execution_without_logging_secrets()
    {
        var mail = new RecordingEmail();
        var logs = new CapturingLoggerProvider();
        using var app = await ReadyAsync(new BriefynFactory { EmailServiceOverride = mail, LogCapture = logs });
        using var client = AnonymousClient(app);
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");
        await PostFormAsync(client, "/Auth/ForgotPassword", antiforgery, ("Input.Email", SeedData.AliceEmail));
        Assert.Empty(mail.Sent);
        var job = Assert.Single(app.Jobs.Created);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(app.Seed.AliceId))!;
        Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);

        await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
            .SendResetEmailAsync((string)job.Args[0]!, (string)job.Args[1]!);

        var sent = Assert.Single(mail.Sent);
        Assert.Equal(user.Email, sent.Address);
        Assert.Equal(user.FullName, sent.Name);
        Assert.Equal("1 saat", sent.Lifetime);
        Assert.StartsWith("http://localhost/Auth/ResetPassword?userId=", sent.Url);
        var query = QueryHelpers.ParseQuery(new Uri(sent.Url).Query);
        Assert.Equal(user.Id, query["userId"]);
        var code = query["code"].ToString();
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        Assert.True(await users.VerifyUserTokenAsync(user, PasswordResetTokenProvider.ProviderName,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token));
        var log = string.Join("\n", logs.Lines);
        Assert.DoesNotContain(SeedData.AliceEmail, log);
        Assert.DoesNotContain(sent.Url, log);
        Assert.DoesNotContain(code, log);
        Assert.DoesNotContain(token, log);
    }

    [Fact]
    public async Task Configured_public_url_overrides_untrusted_host()
    {
        using var app = await ReadyAsync(new BriefynFactory { ExtraSettings = { ["App:PublicUrl"] = "https://briefyn.example/" } });
        using var client = AnonymousClient(app, "saldirgan.example");
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");

        var response = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", SeedData.AliceEmail));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new object[] { app.Seed.AliceId, "https://briefyn.example" }, Assert.Single(app.Jobs.Created).Args);
    }

    [Fact]
    public async Task Enqueue_failure_preserves_generic_response_and_does_not_log_email()
    {
        var logs = new CapturingLoggerProvider();
        using var app = await ReadyAsync(new BriefynFactory { LogCapture = logs });
        using var client = AnonymousClient(app);
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");
        app.Jobs.FailNext = true;

        var known = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", SeedData.AliceEmail));
        var unknown = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", "unknown@example.test"));

        Assert.Equal(HttpStatusCode.Redirect, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        Assert.Empty(app.Jobs.Created);
        var log = string.Join("\n", logs.Lines);
        Assert.Contains("Password reset email could not be queued.", log);
        Assert.DoesNotContain(SeedData.AliceEmail, log);
    }

    [Fact]
    public async Task Signed_in_user_is_redirected_on_get_and_post_without_enqueuing()
    {
        using var app = await ReadyAsync();
        using var client = AnonymousClient(app);
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, app.Seed.AliceId);
        var get = await client.GetAsync("/Auth/ForgotPassword");
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Contains(get.Headers.Location!.OriginalString, new[] { "/", "/Index" });
        // Get an antiforgery token for the authenticated identity from an accessible page.
        token = await GetAntiforgeryTokenAsync(client, "/Profile");
        var post = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", SeedData.AliceEmail));
        Assert.Equal(get.StatusCode, post.StatusCode);
        Assert.Equal(get.Headers.Location, post.Headers.Location);
        Assert.Empty(app.Jobs.Created);
    }

    [Fact]
    public async Task Login_links_to_forgot_password()
    {
        using var app = await ReadyAsync();
        using var client = AnonymousClient(app);
        Assert.Contains("href=\"/Auth/ForgotPassword\"", await client.GetStringAsync("/Auth/Login"));
    }

    [Theory]
    [InlineData(60, "1 saat")]
    [InlineData(90, "90 dakika")]
    [InlineData(120, "2 saat")]
    public void Lifetime_text_uses_hours_only_for_whole_hours(int minutes, string expected) =>
        Assert.Equal(expected, PasswordResetService.LifetimeText(minutes));

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("long")]
    public async Task Invalid_email_is_not_enqueued(string email)
    {
        using var app = await ReadyAsync();
        using var client = AnonymousClient(app);
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/ForgotPassword");
        if (email == "long") email = new string('a', 250) + "@example.test";

        var response = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(app.Jobs.Created);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Job_ignores_missing_user_or_email(bool missingEmail, bool changedEmail)
    {
        var mail = new RecordingEmail();
        using var app = await ReadyAsync(new BriefynFactory { EmailServiceOverride = mail });
        if (missingEmail) app.WithDb(db => { db.Users.Single(u => u.Id == app.Seed.AliceId).Email = ""; return db.SaveChanges(); });
        using var scope = app.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        var userId = missingEmail ? app.Seed.AliceId : "missing-user";
        if (changedEmail) await service.SendPasswordChangedEmailAsync(userId);
        else await service.SendResetEmailAsync(userId, "http://localhost");

        Assert.Empty(mail.Sent);
        Assert.Empty(mail.Changed);
    }

    internal sealed class RecordingEmail : IEmailService
    {
        public List<(string Address, string Name, string Url, string Lifetime)> Sent { get; } = [];
        public Task SendPasswordResetAsync(string toEmail, string fullName, string resetUrl, string lifetimeText)
        {
            Sent.Add((toEmail, fullName, resetUrl, lifetimeText));
            return Task.CompletedTask;
        }
        public List<(string Address, string Name)> Changed { get; } = [];
        public Task SendPasswordChangedAsync(string toEmail, string fullName)
        {
            Changed.Add((toEmail, fullName));
            return Task.CompletedTask;
        }
        public Task SendMeetingSummaryAsync(int meetingId, string protectedRecipient) => Task.CompletedTask;
        public Task SendSeriesAgendaAsync(int seriesId, string protectedRecipient) => Task.CompletedTask;
        public Task SendOverdueReminderAsync(string toEmail, string fullName, IReadOnlyList<OverdueTask> tasks) => Task.CompletedTask;
        public Task SendCalendarInviteAsync(CalendarInviteData invite, CalendarMethod method) => Task.CompletedTask;
    }

    [Fact]
    public async Task Host_poisoning_does_not_enqueue_or_log_personal_data()
    {
        var logs = new CapturingLoggerProvider();
        using var app = await ReadyAsync(new ProductionLoggingFactory { LogCapture = logs, ExtraSettings = { ["App:PublicUrl"] = "" } });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://saldirgan.example"),
            AllowAutoRedirect = false
        });
        var token = await GetAntiforgeryTokenAsync(client, "/Auth/Login");

        var response = await PostFormAsync(client, "/Auth/ForgotPassword", token, ("Input.Email", SeedData.AliceEmail));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(app.Jobs.Created);
        var log = string.Join("\n", logs.Lines);
        Assert.Contains("Password reset email was not sent: App:PublicUrl is empty and the request did not come from a loopback address.", log);
        Assert.DoesNotContain(SeedData.AliceEmail, log);
        Assert.DoesNotContain("saldirgan", log);
    }

    [Fact]
    public void Reset_email_text_contains_exact_wording_url_and_lifetime()
    {
        const string url = "https://briefyn.example/Auth/ResetPassword?userId=123&code=abc";
        Assert.Equal($"Merhaba Ali,\n\nBriefyn AI hesabınız için şifre yenileme isteği aldık. Yeni şifrenizi belirlemek için bu bağlantıyı açın:\n{url}\n\nBağlantı 1 saat geçerli ve yalnız bir kez kullanılabilir.\n\nBu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz; şifreniz değişmez.",
            EmailService.BuildPasswordResetText("Ali", url, "1 saat"));
    }

    [Fact]
    public void Reset_email_html_encodes_all_interpolated_values()
    {
        var html = EmailService.BuildPasswordResetHtml("<b>Ali</b>", "https://briefyn.example/?a=1&b=\"x\"", "<i>1 saat</i>");
        Assert.Contains("&lt;b&gt;Ali&lt;/b&gt;", html);
        Assert.Contains("href=\"https://briefyn.example/?a=1&amp;b=&quot;x&quot;\"", html);
        Assert.Contains("&lt;i&gt;1 saat&lt;/i&gt;", html);
        Assert.DoesNotContain("<b>Ali</b>", html);
    }

    [Fact]
    public void Changed_email_has_exact_text_and_encodes_html_name()
    {
        Assert.Equal("Merhaba Ali,\n\nBriefyn AI hesabınızın şifresi az önce değiştirildi. Açık olan bütün oturumlarınız kapatıldı.\n\nBunu siz yapmadıysanız hemen bu e-postayı yanıtlayarak bize yazın.",
            EmailService.BuildPasswordChangedText("Ali"));
        var html = EmailService.BuildPasswordChangedHtml("<b>Ali</b>");
        Assert.Contains("&lt;b&gt;Ali&lt;/b&gt;", html);
        Assert.Contains("Açık olan bütün oturumlarınız kapatıldı.", html);
        Assert.Contains("Bunu siz yapmadıysanız hemen bu e-postayı yanıtlayarak bize yazın.", html);
    }

    private static async Task<BriefynFactory> ReadyAsync(BriefynFactory? app = null)
    {
        app ??= new BriefynFactory();
        await app.InitializeAsync();
        return app;
    }

    internal sealed class ProductionLoggingFactory : BriefynFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                // BriefynFactory's capture bypasses Serilog; mirror Program's production framework filters.
                var filters = new LoggerFilterOptions();
                filters.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                filters.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                services.AddSingleton<ILoggerFactory>(new LoggerFactory(new[] { LogCapture! }, filters));
            });
        }
    }

    [Theory]
    [InlineData(null, "saldirgan.example", null)]
    [InlineData("", "10.0.0.5", null)]
    [InlineData(" ", "localhost.saldirgan.example", null)]
    [InlineData("ftp://briefyn.example", "localhost", null)]
    [InlineData("garbage", "localhost", null)]
    [InlineData("https://briefyn.example", "localhost", "https://briefyn.example")]
    [InlineData("https://briefyn.example/", "localhost", "https://briefyn.example")]
    [InlineData("http://briefyn.example/", "saldirgan.example", "http://briefyn.example")]
    [InlineData(null, "localhost:8080", "http://localhost:8080")]
    [InlineData("", "LOCALHOST", "http://LOCALHOST")]
    [InlineData(" ", "127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData(null, "[::1]:8080", "http://[::1]:8080")]
    public void Base_url_uses_valid_public_url_or_loopback_only(string? publicUrl, string host, string? expected)
    {
        Assert.Equal(expected, PasswordResetLink.BaseUrl(publicUrl, "http", new HostString(host)));
    }

    [Fact]
    public void Reset_url_escapes_user_id_and_preserves_base64url_code()
    {
        Assert.Equal("https://briefyn.example/Auth/ResetPassword?userId=user%2Bname%2F%3F%26%3D%20%C3%BC&code=encoded_code-123",
            PasswordResetLink.ResetUrl("https://briefyn.example", "user+name/?&= ü", "encoded_code-123"));
    }

    [Fact]
    public async Task Password_reset_uses_dedicated_provider()
    {
        using var app = await ReadyAsync();
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.Equal("PasswordReset", users.Options.Tokens.PasswordResetTokenProvider);
    }

    [Fact]
    public async Task Password_reset_lifetime_is_one_hour_without_changing_default_provider()
    {
        using var app = await ReadyAsync();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.Equal(TimeSpan.FromDays(1), services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        var resetOptions = services.GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(60), resetOptions.TokenLifespan);
        Assert.Equal("PasswordReset", resetOptions.Name);
        Assert.Equal(60, services.GetRequiredService<IOptions<PasswordResetOptions>>().Value.LinkLifetimeMinutes);
    }

    [Fact]
    public async Task Generated_password_reset_token_verifies_with_selected_provider()
    {
        using var app = await ReadyAsync();
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(app.Seed.AliceId))!;

        var token = await users.GeneratePasswordResetTokenAsync(user);

        Assert.Equal("PasswordReset", users.Options.Tokens.PasswordResetTokenProvider);
        Assert.True(await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token));
    }

    [Fact]
    public async Task Expired_password_reset_token_is_rejected()
    {
        // .NET 10's provider uses DateTimeOffset.UtcNow, not DI TimeProvider; a negative lifespan expires the token immediately.
        using var app = await ReadyAsync(new BriefynFactory { ExtraSettings = { ["PasswordReset:LinkLifetimeMinutes"] = "-1" } });
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(app.Seed.AliceId))!;

        var token = await users.GeneratePasswordResetTokenAsync(user);

        Assert.False(await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token));
        Assert.Equal(-1, scope.ServiceProvider.GetRequiredService<IOptions<PasswordResetOptions>>().Value.LinkLifetimeMinutes);
    }

    [Fact]
    public async Task Successful_password_reset_invalidates_same_token_through_security_stamp()
    {
        using var app = await ReadyAsync();
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(app.Seed.AliceId))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var stamp = await users.GetSecurityStampAsync(user);
        Assert.Equal("PasswordReset", users.Options.Tokens.PasswordResetTokenProvider);
        Assert.True(await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token));

        var result = await users.ResetPasswordAsync(user, token, "New-Parola-123");

        Assert.True(result.Succeeded);
        Assert.NotEqual(stamp, await users.GetSecurityStampAsync(user));
        Assert.False(await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token));
    }
}
