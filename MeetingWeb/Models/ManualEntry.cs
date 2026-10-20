namespace MeetingWeb.Models
{
    // Topics, decisions and tasks a user adds by hand are stored as "text|~|authorId" (AI-generated ones have no author).
    // The format is persisted in encrypted columns, so the separator must not change.
    public static class ManualEntry
    {
        public const string Separator = "|~|";

        public static string Create(string text, string authorId) => $"{text}{Separator}{authorId}";

        public static bool IsManual(string? value) => value != null && value.Contains(Separator);

        public static string TextOf(string value) => value.Split(Separator)[0];

        public static string? AuthorOf(string value)
        {
            var parts = value.Split(Separator);
            return parts.Length > 1 ? parts[1] : null;
        }
    }
}
