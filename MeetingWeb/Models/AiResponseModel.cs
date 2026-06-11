using System.Text.Json.Serialization;

namespace MeetingWeb.Models
{
    // Root Data Transfer Object (DTO) mapping the external Python API response.
    public class AiApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("transkript")]
        public string Transkript { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public AiSummaryData Summary { get; set; } = new();
    }

    // Nested DTO encapsulating the structured AI extraction results.
    public class AiSummaryData
    {
        [JsonPropertyName("toplanti_konusu")]
        public string ToplantiKonusu { get; set; } = string.Empty;

        [JsonPropertyName("gorusulen_konular")]
        public List<string> GorusulenKonular { get; set; } = new();

        [JsonPropertyName("alinan_kararlar")]
        public List<string> AlinanKararlar { get; set; } = new();

        [JsonPropertyName("aksiyon_maddeleri")]
        public List<string> AksiyonMaddeleri { get; set; } = new();
    }
}