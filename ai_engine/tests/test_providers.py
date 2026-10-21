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
