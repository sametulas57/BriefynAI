using System.Text;
using Hangfire;
using MeetingWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public interface IPasswordResetService
    {
        Task RequestAsync(string email, string? baseUrl);
        Task SendResetEmailAsync(string userId, string baseUrl);
    }

    public class PasswordResetService : IPasswordResetService
    {
        public const string EmailQueue = "eposta";

        private readonly UserManager<ApplicationUser> _users;
        private readonly IBackgroundJobClient _jobs;
        private readonly IEmailService _email;
        private readonly PasswordResetOptions _options;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(UserManager<ApplicationUser> users, IBackgroundJobClient jobs,
            IEmailService email, IOptions<PasswordResetOptions> options, ILogger<PasswordResetService> logger)
        {
            _users = users;
            _jobs = jobs;
            _email = email;
            _options = options.Value;
            _logger = logger;
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

        public static string LifetimeText(int minutes) =>
            minutes % 60 == 0 ? $"{minutes / 60} saat" : $"{minutes} dakika";
    }
}
