using System.Text;
using Hangfire;
using MeetingWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public enum PasswordResetOutcome { Succeeded, InvalidLink, PasswordRejected }

    public sealed record PasswordResetResult(PasswordResetOutcome Outcome, IReadOnlyList<string> Errors);

    public interface IPasswordResetService
    {
        Task<PasswordResetResult> ResetAsync(string? userId, string? encodedCode, string newPassword);
        Task<bool> IsLinkValidAsync(string? userId, string? encodedCode);
        Task RequestAsync(string email, string? baseUrl);
        Task SendResetEmailAsync(string userId, string baseUrl);
        Task SendPasswordChangedEmailAsync(string userId);
    }

    public class PasswordResetService : IPasswordResetService
    {
        public const string EmailQueue = "eposta";

        private readonly UserManager<ApplicationUser> _users;
        private readonly IBackgroundJobClient _jobs;
        private readonly IEmailService _email;
        private readonly PasswordResetOptions _options;
        private readonly PasswordResetRateLimiter _limiter;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(UserManager<ApplicationUser> users, IBackgroundJobClient jobs,
            IEmailService email, IOptions<PasswordResetOptions> options, ILogger<PasswordResetService> logger,
            PasswordResetRateLimiter limiter)
        {
            _users = users;
            _jobs = jobs;
            _email = email;
            _options = options.Value;
            _logger = logger;
            _limiter = limiter;
        }

        public async Task RequestAsync(string email, string? baseUrl)
        {
            if (baseUrl == null)
            {
                _logger.LogWarning("Password reset email was not sent: App:PublicUrl is empty and the request did not come from a loopback address.");
                return;
            }

            var user = await _users.FindByEmailAsync(email);
            if (user == null || string.IsNullOrEmpty(user.Email)) return;
            if (!_limiter.TryAccept(user.Id)) return;

            try
            {
                _jobs.Enqueue<IPasswordResetService>(EmailQueue, s => s.SendResetEmailAsync(user.Id, baseUrl));
            }
            catch (Exception)
            {
                // Exception details may contain personal data; preserve the generic response and log only the event.
                _logger.LogError("Password reset email could not be queued.");
            }
        }

        public async Task SendResetEmailAsync(string userId, string baseUrl)
        {
            var user = await _users.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.Email)) return;

            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var url = PasswordResetLink.ResetUrl(baseUrl, user.Id, encoded);
            await _email.SendPasswordResetAsync(user.Email, user.FullName, url, LifetimeText(_options.LinkLifetimeMinutes));
        }

        public async Task<bool> IsLinkValidAsync(string? userId, string? encodedCode)
        {
            var token = DecodeCode(encodedCode);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(token)) return false;
            var user = await _users.FindByIdAsync(userId);
            return user != null && await _users.VerifyUserTokenAsync(user, _users.Options.Tokens.PasswordResetTokenProvider,
                UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token);
        }

        public async Task<PasswordResetResult> ResetAsync(string? userId, string? encodedCode, string newPassword)
        {
            var token = DecodeCode(encodedCode);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(token))
                return new(PasswordResetOutcome.InvalidLink, []);
            var user = await _users.FindByIdAsync(userId);
            if (user == null) return new(PasswordResetOutcome.InvalidLink, []);

            if (!await _users.VerifyUserTokenAsync(user, _users.Options.Tokens.PasswordResetTokenProvider,
                UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token))
                return new(PasswordResetOutcome.InvalidLink, []);
            if (await _users.CheckPasswordAsync(user, newPassword))
                return new(PasswordResetOutcome.PasswordRejected,
                    ["Yeni şifreniz eskisiyle aynı olamaz. Lütfen farklı bir şifre seçin."]);

            var result = await _users.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
                return result.Errors.Any(e => e.Code == "InvalidToken")
                    ? new(PasswordResetOutcome.InvalidLink, [])
                    : new(PasswordResetOutcome.PasswordRejected, result.Errors.Select(e => e.Description).ToArray());

            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
            try
            {
                _jobs.Enqueue<IPasswordResetService>(EmailQueue, s => s.SendPasswordChangedEmailAsync(user.Id));
            }
            catch (Exception)
            {
                // Job storage failures must not undo the reset or expose personal data in exception details.
                _logger.LogError("Password changed email could not be queued.");
            }
            return new(PasswordResetOutcome.Succeeded, []);
        }

        public async Task SendPasswordChangedEmailAsync(string userId)
        {
            var user = await _users.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.Email)) return;
            await _email.SendPasswordChangedAsync(user.Email, user.FullName);
        }

        private static string? DecodeCode(string? encodedCode)
        {
            if (string.IsNullOrWhiteSpace(encodedCode)) return null;
            try
            {
                return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedCode));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static string LifetimeText(int minutes) =>
            minutes % 60 == 0 ? $"{minutes / 60} saat" : $"{minutes} dakika";
    }
}
