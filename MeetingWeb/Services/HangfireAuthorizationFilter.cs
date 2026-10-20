using Hangfire.Dashboard;

namespace MeetingWeb.Services
{
    // Custom Authorization Filter for Hangfire Dashboard.
    // S6: the dashboard shows job arguments (meeting ids, file paths) and can delete or re-run jobs, so being signed in
    // is not enough. Only operators listed in Hangfire:DashboardAdminEmails get in; an empty list locks everyone out.
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            var user = httpContext.User;
            if (user.Identity?.IsAuthenticated != true) return false;

            var admins = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                .GetSection("Hangfire:DashboardAdminEmails").Get<string[]>() ?? Array.Empty<string>();

            // Accounts are created with UserName == Email, so the Name claim is a valid fallback.
            var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.Identity.Name;

            return !string.IsNullOrWhiteSpace(email) &&
                   admins.Any(a => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), email, StringComparison.OrdinalIgnoreCase));
        }
    }
}
