using System.Security.Claims;
using MeetingWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    // Copies the profile name and photo into the sign-in cookie, so the layout shows them without a query.
    // Pages that change the profile call RefreshSignInAsync to rebuild the cookie.
    public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>
    {
        public AppClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> options)
            : base(userManager, options)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);
            identity.AddClaim(new Claim(UserClaimTypes.FullName, user.FullName));
            if (user.AvatarFileName != null) identity.AddClaim(new Claim(UserClaimTypes.AvatarUrl, UserDisplay.AvatarUrl(user.AvatarFileName)));
            return identity;
        }
    }
}
