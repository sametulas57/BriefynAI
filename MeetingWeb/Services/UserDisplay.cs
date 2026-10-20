namespace MeetingWeb.Services
{
    // How users are shown: name fallback, initials for the avatar placeholder, and the public photo URL.
    public static class UserDisplay
    {
        // Shown wherever a user has no name on their profile.
        public const string UnknownName = "İsimsiz Üye";

        // Avatars are the only public uploads (see the /uploads guard in Program.cs).
        public const string AvatarUrlPath = "/uploads/avatars";

        public static string AvatarUrl(string? avatarFileName) =>
            string.IsNullOrEmpty(avatarFileName) ? "" : $"{AvatarUrlPath}/{avatarFileName}";

        public static string Initials(string fullName)
        {
            var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return nameParts.Length > 1
                ? (nameParts[0][0].ToString() + nameParts[^1][0].ToString()).ToUpper()
                : fullName.Substring(0, Math.Min(fullName.Length, 2)).ToUpper();
        }
    }
}
