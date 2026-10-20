using System.Security.Claims;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Storage;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Security
{
    // S6: the Hangfire dashboard exposes job arguments (meeting ids, file paths) and can delete or re-run jobs,
    // so only operators listed in Hangfire:DashboardAdminEmails may open it. An empty list locks everyone out.
    public class HangfireDashboardTests
    {
        private static bool Authorize(ClaimsPrincipal user, params string[] adminEmails)
        {
            var settings = adminEmails
                .Select((email, i) => new KeyValuePair<string, string?>($"Hangfire:DashboardAdminEmails:{i}", email));
            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
                .BuildServiceProvider();
            var http = new DefaultHttpContext { User = user, RequestServices = services };
            var context = new AspNetCoreDashboardContext(new NullJobStorage(), new DashboardOptions(), http);

            return new HangfireAuthorizationFilter().Authorize(context);
        }

        private static ClaimsPrincipal SignedIn(string email) => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email)
        }, "Test"));

        [Fact]
        public void Anonymous_user_is_rejected() =>
            Assert.False(Authorize(new ClaimsPrincipal(new ClaimsIdentity()), "admin@briefyn.test"));

        [Fact]
        public void Ordinary_signed_in_user_is_rejected() =>
            Assert.False(Authorize(SignedIn("alice@a.test"), "admin@briefyn.test"));

        [Fact]
        public void Nobody_gets_in_when_no_admin_is_configured() =>
            Assert.False(Authorize(SignedIn("alice@a.test")));

        [Fact]
        public void Listed_admin_is_allowed_case_insensitively() =>
            Assert.True(Authorize(SignedIn("Admin@Briefyn.test"), "admin@briefyn.test"));

        // The dashboard context only needs a storage instance; nothing here talks to it.
        private sealed class NullJobStorage : JobStorage
        {
            public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();
            public override IStorageConnection GetConnection() => throw new NotSupportedException();
        }
    }
}
