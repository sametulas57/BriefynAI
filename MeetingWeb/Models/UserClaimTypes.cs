namespace MeetingWeb.Models
{
    // Profile values copied into the sign-in cookie by AppClaimsPrincipalFactory, so the layout can show them
    // without a database query. The source of truth is ApplicationUser.
    public static class UserClaimTypes
    {
        public const string FullName = "FullName";
        public const string AvatarUrl = "AvatarUrl";
    }
}
