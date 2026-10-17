using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MeetingWeb.Tests.Infrastructure;

namespace MeetingWeb.Tests.Security
{
    // S9b: login cookies, antiforgery tokens and TempData are protected with Data Protection keys. Keys kept inside
    // the container were lost on every rebuild, logging everyone out and breaking open forms. They must persist.
    public class DataProtectionTests
    {
        private static string Protect(BriefynFactory app, string value) =>
            app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect(value);

        private static string Unprotect(BriefynFactory app, string value) =>
            app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(value);

        [Fact]
        public void A_restarted_app_still_reads_what_the_previous_one_protected()
        {
            var keys = Path.Combine(Path.GetTempPath(), "briefyn-keys-" + Guid.NewGuid().ToString("N"));
            string ticket;
            using (var before = new BriefynFactory { KeysPath = keys })
            {
                ticket = Protect(before, "oturum");
            }

            using var after = new BriefynFactory { KeysPath = keys }; // same folder = same volume after a rebuild
            Assert.Equal("oturum", Unprotect(after, ticket));
            Assert.NotEmpty(Directory.GetFiles(keys, "*.xml"));
        }
    }
}
