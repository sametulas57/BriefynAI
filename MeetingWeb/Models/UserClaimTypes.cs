namespace MeetingWeb.Models
{
    // Profile data kept as Identity claims (AspNetUserClaims). Stored in the database, so the names must not change.
    public static class UserClaimTypes
    {
        public const string FullName = "FullName";
        public const string AvatarUrl = "AvatarUrl";

        // Older accounts may still carry their photo under this name; it is only read.
        public const string LegacyProfilePicture = "ProfilePicture";
    }
}
