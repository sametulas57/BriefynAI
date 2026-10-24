namespace MeetingWeb.Services.ArchiveSearch;

public record AskResult(AskStatus Status, int MeetingsSearched, string? Answer, string? Uncertain,
    IReadOnlyList<AskSource> Sources)
{
    public string? Message => Status switch
    {
        AskStatus.Answered => null,
        AskStatus.NotFound => "Arşivde bununla ilgili bir şey bulamadım.",
        AskStatus.EmptyArchive => "Bu kapsamda bakılacak tamamlanmış toplantı yok.",
        AskStatus.EmptyQuestion => "Lütfen bir soru yazın.",
        AskStatus.QuestionTooLong => "Soru en fazla 500 karakter olabilir.",
        AskStatus.RateLimited => "Bu saat içinde çok fazla soru sordunuz. Lütfen biraz sonra tekrar deneyin.",
        AskStatus.Busy => "Yapay zekâ şu an yanıt veremiyor. Lütfen birazdan tekrar deneyin.",
        _ => null
    };
}
