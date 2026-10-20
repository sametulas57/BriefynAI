namespace MeetingWeb.Services
{
    // Avatar fallback shown when a user has no photo: first letters of the first and last name, or the first two letters.
    public static class UserDisplay
    {
        // Shown wherever a user has no name on their profile.
        public const string UnknownName = "İsimsiz Üye";

        public static string Initials(string fullName)
        {
            var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return nameParts.Length > 1
                ? (nameParts[0][0].ToString() + nameParts[^1][0].ToString()).ToUpper()
                : fullName.Substring(0, Math.Min(fullName.Length, 2)).ToUpper();
        }
    }
}
