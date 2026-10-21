"""Meeting summary with a Groq-hosted LLM and a strict JSON schema.

Model choice (2026-10-16 blind test, 5 tricky fake meetings): qwen/qwen3.8-27b made 0 fabrications;
gpt-oss-120b ~6 and the old local qwen2.5:14b ~7. Long transcripts are summarised in parts and merged,
which also keeps every request under the free tier's per-minute token limit.
"""
import json
import logging

from .groq_client import GroqClient, GroqError  # noqa: F401  (GroqError re-exported for callers)

SYSTEM = (
    "Toplantı dökümünden yapılandırılmış özet çıkaran bir asistansın. Yalnız dökümde geçen bilgiyi kullan; "
    "dökümde olmayan kişi, tarih, rakam ekleme. Döküm otomatik yazıya dökülmüştür, küçük yazım hataları olabilir; "
    "bariz olanları düzelt, emin olmadığını olduğu gibi bırak. Sonradan değiştirilen kararlarda son hâli yaz; "
    "önerilip kabul edilmeyen ya da ertelenen konuları karar diye yazma. Aksiyonun sahibi ya da süresi "
    "söylenmemişse null bırak. Türkçe yaz. Dökümde satırlar Ad: söz biçiminde olabilir; "
    "aksiyonun sahibini bu adlardan yalnız dökümde açıkça söylendiyse yaz."
)

SCHEMA = {
    "type": "object", "additionalProperties": False,
    "required": ["toplanti_konusu", "gorusulen_konular", "alinan_kararlar", "aksiyon_maddeleri"],
    "properties": {
        "toplanti_konusu": {"type": "string"},
        "gorusulen_konular": {"type": "array", "items": {"type": "string"}},
        "alinan_kararlar": {"type": "array", "items": {"type": "string"}},
        "aksiyon_maddeleri": {"type": "array", "items": {
            "type": "object", "additionalProperties": False, "required": ["kim", "ne", "ne_zaman"],
            "properties": {"kim": {"type": ["string", "null"]}, "ne": {"type": "string"}, "ne_zaman": {"type": ["string", "null"]}}}},
    },
}

MERGE_INSTRUCTION = (
    "Aşağıda aynı toplantının ardışık bölümlerinden çıkarılmış kısmi özetler var. Bunları tek bir özette birleştir: "
    "tekrarları ayıkla, sonraki bölümde değişen kararlarda son hâli yaz, kısmi özetlerde olmayan hiçbir şey ekleme."
)


def split_text(text: str, max_chars: int) -> list[str]:
    """Splits on sentence ends so no sentence is cut in half."""
    sentences = [s.strip() + "." for s in text.replace("\n", " ").split(". ") if s.strip()]
    parts, current = [], ""
    for sentence in sentences:
        sentence = sentence[:-1] if sentence.endswith("..") else sentence
        if current and len(current) + len(sentence) + 1 > max_chars:
            parts.append(current.strip())
            current = ""
        current += sentence + " "
    if current.strip():
        parts.append(current.strip())
    return parts


def to_backend_format(summary: dict) -> dict:
    """The .NET backend stores action items as text lines: 'Kim: ne (ne zaman)'."""
    def line(action: dict) -> str:
        text = action.get("ne", "").strip()
        if action.get("kim"):
            text = f"{action['kim']}: {text}"
        if action.get("ne_zaman"):
            text = f"{text} ({action['ne_zaman']})"
        return text

    return {
        "toplanti_konusu": summary.get("toplanti_konusu", "").strip(),
        "gorusulen_konular": summary.get("gorusulen_konular", []),
        "alinan_kararlar": summary.get("alinan_kararlar", []),
        "aksiyon_maddeleri": [line(a) for a in summary.get("aksiyon_maddeleri", []) if isinstance(a, dict)],
    }


class GroqSummarizer:
    def __init__(self, client: GroqClient, model: str = "qwen/qwen3.8-27b", max_chars: int = 12000):
        self._client, self._model, self._max_chars = client, model, max_chars

    def _ask(self, content: str) -> dict:
        return self._client.chat_json(self._model, [{"role": "system", "content": SYSTEM},
                                                    {"role": "user", "content": content}], SCHEMA)

    def summarize(self, transcript: str) -> dict:
        if len(transcript) <= self._max_chars:
            return to_backend_format(self._ask(f"Toplantı dökümü:\n\n{transcript}"))

        parts = split_text(transcript, self._max_chars)
        logging.info(f"Long transcript: summarising {len(parts)} parts, then merging.")
        partials = [self._ask(f"Toplantı dökümünün {i + 1}/{len(parts)}. bölümü:\n\n{p}") for i, p in enumerate(parts)]
        merged = self._ask(f"{MERGE_INSTRUCTION}\n\nKısmi özetler:\n{json.dumps(partials, ensure_ascii=False)}")
        return to_backend_format(merged)
