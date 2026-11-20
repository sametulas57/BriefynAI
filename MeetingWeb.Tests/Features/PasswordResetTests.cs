using MeetingWeb.Models;
using MeetingWeb.Services;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Tests.Features;

public class PasswordResetTests
{
    private static async Task<BriefynFactory> ReadyAsync(BriefynFactory? app = null)
    {
        app ??= new BriefynFactory();
        await app.InitializeAsync();
        return app;
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
