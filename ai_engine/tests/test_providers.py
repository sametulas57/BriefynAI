"""Faz 2: Groq-backed transcription and summarisation, tested without network (httpx.MockTransport)."""
import json
import pathlib
import sys

import httpx
import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from providers.audio import plan_cuts  # noqa: E402
from providers.groq_client import GroqClient, GroqError  # noqa: E402
from providers.groq_summarizer import GroqSummarizer, split_text, to_backend_format  # noqa: E402


def client_with(handler, sleeps=None):
    sleeps = [] if sleeps is None else sleeps
    return GroqClient("test-key", http=httpx.Client(transport=httpx.MockTransport(handler)), sleep=sleeps.append)


# --- audio chunking (Groq dropped Turkish letters on long single uploads; ~60 s pieces fixed it) ---

def test_cuts_land_on_silences_near_the_target():
    cuts = plan_cuts(duration=234.0, silences=[59.3, 120.8, 179.5], target=60)
    assert cuts == [0.0, 59.3, 120.8, 179.5, 234.0]


def test_cuts_fall_back_to_fixed_length_without_silences():
    cuts = plan_cuts(duration=150.0, silences=[], target=60)
    assert cuts == [0.0, 60.0, 120.0, 150.0]


def test_short_audio_is_one_piece():
    assert plan_cuts(duration=40.0, silences=[20.0], target=60) == [0.0, 40.0]


# --- Groq client ---

def test_client_sends_the_key_and_waits_on_rate_limit():
    calls, sleeps = [], []

    def handler(request):
        calls.append(request)
        if len(calls) == 1:
            return httpx.Response(429, headers={"retry-after": "7"}, json={"error": "rate"})
        return httpx.Response(200, json={"choices": [{"message": {"content": "{\"a\": 1}"}}]})

    result = client_with(handler, sleeps).chat_json("m", [{"role": "user", "content": "x"}], {"type": "object"})

    assert result == {"a": 1}
    assert calls[0].headers["authorization"] == "Bearer test-key"
    assert sleeps == [7.0]


def test_client_gives_up_with_a_clear_error():
    client = client_with(lambda r: httpx.Response(500, json={"error": "down"}))
    with pytest.raises(GroqError):
        client.chat_json("m", [{"role": "user", "content": "x"}], {"type": "object"})


def test_client_falls_back_to_json_mode_when_schema_is_not_supported():
    bodies = []

    def handler(request):
        body = json.loads(request.content)
        bodies.append(body)
        if body["response_format"]["type"] == "json_schema":
            return httpx.Response(400, json={"error": "schema unsupported"})
        return httpx.Response(200, json={"choices": [{"message": {"content": "{\"ok\": true}"}}]})

    assert client_with(handler).chat_json("m", [{"role": "system", "content": "s"}], {"type": "object"}) == {"ok": True}
    assert bodies[-1]["response_format"]["type"] == "json_object"


def test_timed_transcription_reads_verbose_segments(tmp_path):
    requests = []

    def handler(request):
        requests.append(request)
        return httpx.Response(200, json={"segments": [{"start": 1.2, "end": 3.4, "text": " söz "}]})

    audio = tmp_path / "part.flac"
    audio.write_bytes(b"audio")
    assert client_with(handler).transcribe_segments(audio, "whisper", "tr", "prompt") == [(1.2, 3.4, "söz")]
    assert b'verbose_json' in requests[0].content


# --- summariser ---

SUMMARY = {
    "toplanti_konusu": "Lansman",
    "gorusulen_konular": ["Tedarik"],
    "alinan_kararlar": ["Release salıya"],
    "aksiyon_maddeleri": [{"kim": "Deniz", "ne": "hotfix çıkar", "ne_zaman": "çarşamba"},
                          {"kim": None, "ne": "rollback planı", "ne_zaman": None}],
}


def test_actions_become_readable_lines_for_the_backend():
    data = to_backend_format(SUMMARY)
    assert data["aksiyon_maddeleri"] == ["Deniz: hotfix çıkar (çarşamba)", "rollback planı"]
    assert data["alinan_kararlar"] == ["Release salıya"]


def test_long_transcripts_are_split_on_sentence_boundaries():
    text = "Bir cümle. " * 500
    parts = split_text(text, max_chars=1000)
    assert len(parts) > 1
    assert all(len(p) <= 1000 for p in parts)
    assert all(p.rstrip().endswith(".") for p in parts)


def test_long_transcript_is_summarised_in_parts_then_merged():
    asked = []

    def handler(request):
        body = json.loads(request.content)
        asked.append(body["messages"][-1]["content"])
        return httpx.Response(200, json={"choices": [{"message": {"content": json.dumps(SUMMARY)}}]})

    summariser = GroqSummarizer(client_with(handler), max_chars=1000)
    result = summariser.summarize("Bir cümle. " * 500)

    assert len(asked) > 2                       # several partial summaries + one merge
    assert "Kısmi özetler" in asked[-1]         # the last call merges the partials
    assert result["toplanti_konusu"] == "Lansman"


def test_short_transcript_is_one_call():
    asked = []

    def handler(request):
        asked.append(1)
        return httpx.Response(200, json={"choices": [{"message": {"content": json.dumps(SUMMARY)}}]})

    GroqSummarizer(client_with(handler)).summarize("Kısa toplantı.")
    assert len(asked) == 1


def test_summary_system_excludes_off_topic_small_talk_for_short_transcripts():
    systems = []

    def handler(request):
        body = json.loads(request.content)
        systems.extend(m["content"] for m in body["messages"] if m["role"] == "system")
        return httpx.Response(200, json={"choices": [{"message": {"content": json.dumps(SUMMARY)}}]})

    GroqSummarizer(client_with(handler)).summarize("Maç hakkında kısa sohbet.")
    assert systems and all("işiyle ilgisi olmayan yan sohbet" in system for system in systems)


def test_summary_system_and_merge_keep_off_topic_rule_for_long_transcripts():
    systems, users = [], []

    def handler(request):
        body = json.loads(request.content)
        systems.extend(m["content"] for m in body["messages"] if m["role"] == "system")
        users.extend(m["content"] for m in body["messages"] if m["role"] == "user")
        return httpx.Response(200, json={"choices": [{"message": {"content": json.dumps(SUMMARY)}}]})

    GroqSummarizer(client_with(handler), max_chars=100).summarize("Maç hakkında uzun sohbet. " * 30)
    assert len(systems) > 1
    assert all("işiyle ilgisi olmayan yan sohbet" in system for system in systems)
    assert "işiyle ilgisi olmayan yan sohbet" in users[-1]


# --- transcription cleanup ---

from providers.groq_transcriber import GroqTranscriber, TECH_PROMPT  # noqa: E402


class FakeTranscriptionClient:
    def __init__(self, plain=None, segments=None):
        self.plain, self.segments = plain or [], segments or []

    def transcribe(self, *args):
        return self.plain.pop(0)

    def transcribe_segments(self, *args):
        return self.segments


def fake_audio(monkeypatch, tmp_path, named=False):
    from providers import groq_transcriber
    source = tmp_path / "input.wav"
    source.write_bytes(b"audio")
    monkeypatch.setattr(groq_transcriber.audio, "to_mono_flac", lambda src, dst: (dst.write_bytes(b"mono"), dst)[1])
    monkeypatch.setattr(groq_transcriber.audio, "duration_seconds", lambda path: 60)
    monkeypatch.setattr(groq_transcriber.audio, "silence_midpoints", lambda path: [])
    monkeypatch.setattr(groq_transcriber.audio, "plan_cuts", lambda *args: [0, 60])
    monkeypatch.setattr(groq_transcriber.audio, "cut", lambda src, start, end, dst: (dst.write_bytes(b"part"), dst)[1])
    return source


def test_segmented_transcription_drops_prompt_and_known_hallucinations(monkeypatch, tmp_path):
    from providers.groq_transcriber import OLD_TECH_INSTRUCTION
    source = fake_audio(monkeypatch, tmp_path)
    client = FakeTranscriptionClient(segments=[
        (0, 1, "Altyazı M.K."), (1, 2, OLD_TECH_INSTRUCTION), (2, 3, "Gerçek sözler burada.")
    ])
    result = GroqTranscriber(client, "whisper").transcribe(str(source), "WEBVTT\n\n00:00:00.000 --> 00:00:03.000\nBucky Barnes: Merhaba")
    assert result == "Bucky Barnes: Gerçek sözler burada."


def test_segmented_transcription_keeps_real_speech_that_looks_like_the_hint_or_a_caption(monkeypatch, tmp_path):
    source = fake_audio(monkeypatch, tmp_path)
    client = FakeTranscriptionClient(segments=[(0, 1, "Docker."), (1, 2, "Docker, Kubernetes, GitHub."),
                                               (2, 3, "Altyazı desteğini ekleyelim."), (3, 4, "Altyazı M.K.")])
    result = GroqTranscriber(client, "whisper").transcribe(str(source), "WEBVTT\n\n00:00:00.000 --> 00:00:04.000\nBucky Barnes: Merhaba")
    assert result == "Bucky Barnes: Docker. Docker, Kubernetes, GitHub. Altyazı desteğini ekleyelim."


def test_plain_transcription_drops_a_final_caption_credit_with_initials(monkeypatch, tmp_path):
    source = fake_audio(monkeypatch, tmp_path)
    client = FakeTranscriptionClient(plain=["Gerçek konuşma. Altyazı M.K."])
    assert GroqTranscriber(client, "whisper").transcribe(str(source)) == "Gerçek konuşma."


def test_plain_transcription_drops_final_hallucination_only(monkeypatch, tmp_path):
    source = fake_audio(monkeypatch, tmp_path)
    text = "Gerçek konuşma. İzlediğiniz için teşekkür ederim."
    client = FakeTranscriptionClient(plain=[text])
    assert GroqTranscriber(client, "whisper").transcribe(str(source)) == "Gerçek konuşma."


def test_plain_transcription_keeps_hallucination_phrase_inside_sentence(monkeypatch, tmp_path):
    source = fake_audio(monkeypatch, tmp_path)
    text = "Bunu izlediğiniz için teşekkür ederim dedi ve devam etti."
    client = FakeTranscriptionClient(plain=[text])
    assert GroqTranscriber(client, "whisper").transcribe(str(source)) == text


def test_plain_transcription_drops_prompt_as_complete_chunk(monkeypatch, tmp_path):
    source = fake_audio(monkeypatch, tmp_path)
    from providers.groq_transcriber import OLD_TECH_INSTRUCTION
    client = FakeTranscriptionClient(plain=[OLD_TECH_INSTRUCTION])
    assert GroqTranscriber(client, "whisper").transcribe(str(source)) == ""


def test_tech_prompt_is_a_term_list_not_an_instruction():
    assert "backend" in TECH_PROMPT and "CI/CD" in TECH_PROMPT
    assert "İngilizce" not in TECH_PROMPT
    assert ":" not in TECH_PROMPT
