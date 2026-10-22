"""S5: the AI engine must only serve the .NET backend (shared key), cap upload size,
never build file paths from the client's file name, and never leak internal errors."""
import pathlib
import sys

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import api_server  # noqa: E402

KEY = "test-ai-engine-key"
URL = "/api/process-meeting"
SUMMARY = {"toplanti_konusu": "x", "gorusulen_konular": [], "alinan_kararlar": [], "aksiyon_maddeleri": []}


@pytest.fixture
def seen_paths():
    return []


@pytest.fixture
def client(monkeypatch, seen_paths):
    monkeypatch.setenv("AI_ENGINE_API_KEY", KEY)
    monkeypatch.setenv("AI_ENGINE_MAX_UPLOAD_MB", "1")

    def fake_transcribe(path):
        seen_paths.append(path)
        return "metin"

    monkeypatch.setattr(api_server, "transcribe_audio", fake_transcribe)
    monkeypatch.setattr(api_server, "generate_meeting_summary", lambda text: SUMMARY)
    return TestClient(api_server.app)


def upload(client, headers=None, name="kayit.m4a", size=10):
    return client.post(URL, headers=headers or {}, files={"file": (name, b"\0" * size, "audio/mp4")})


def test_request_without_key_is_rejected(client):
    assert upload(client).status_code == 401


def test_request_with_wrong_key_is_rejected(client):
    assert upload(client, {"X-Api-Key": "yanlis"}).status_code == 401


def test_correct_key_is_accepted(client):
    res = upload(client, {"X-Api-Key": KEY})
    assert res.status_code == 200
    assert res.json()["success"] is True


@pytest.mark.parametrize("transcript", [
    None, "", " \n ", "Altyazı M.K.", "  altyazı   m k !! ",
    "İZLEDİĞİNİZ İÇİN TEŞEKKÜRLER!", "İzlediğiniz için teşekkür ederim.",
    "Altyazı M.K. İzlediğiniz için teşekkürler!",
])
def test_no_speech_returns_distinct_response(client, monkeypatch, transcript):
    monkeypatch.setattr(api_server, "transcribe_audio", lambda path: transcript)
    monkeypatch.setattr(api_server, "generate_meeting_summary", lambda text: pytest.fail("silence must not be summarized"))

    response = upload(client, {"X-Api-Key": KEY})

    assert response.status_code == 422
    assert response.json() == {"detail": "no_speech"}


def test_caption_with_real_speech_is_processed(client, monkeypatch):
    monkeypatch.setattr(api_server, "transcribe_audio", lambda path: "Altyazı M.K. Bugün toplantıyı açıyorum.")

    response = upload(client, {"X-Api-Key": KEY})

    assert response.status_code == 200
    assert response.json()["transkript"] == "Altyazı M.K. Bugün toplantıyı açıyorum."


def test_optional_speakers_are_passed_to_transcriber(client, monkeypatch):
    seen = []
    monkeypatch.setattr(api_server, "transcribe_audio", lambda path, speakers_vtt=None: seen.append(speakers_vtt) or "metin")
    headers = {"X-Api-Key": KEY}
    assert upload(client, headers).status_code == 200
    response = client.post(URL, headers=headers, files={
        "file": ("audio.m4a", b"audio", "audio/mp4"),
        "speakers": ("names.vtt", "WEBVTT\n\nAli: söz".encode(), "text/vtt"),
    })
    assert response.status_code == 200
    assert seen == [None, "WEBVTT\n\nAli: söz"]


def test_speakers_over_two_megabytes_are_rejected(client, monkeypatch):
    monkeypatch.setattr(api_server, "max_upload_bytes", lambda: 10 * 1024 * 1024)
    response = client.post(URL, headers={"X-Api-Key": KEY}, files={
        "file": ("audio.m4a", b"audio", "audio/mp4"),
        "speakers": ("names.vtt", b"a" * (2 * 1024 * 1024 + 1), "text/vtt"),
    })
    assert response.status_code == 413


def test_invalid_utf8_speakers_are_ignored(client, monkeypatch):
    seen = []
    monkeypatch.setattr(api_server, "transcribe_audio", lambda path, speakers_vtt=None: seen.append(speakers_vtt) or "metin")
    response = client.post(URL, headers={"X-Api-Key": KEY}, files={
        "file": ("audio.m4a", b"audio", "audio/mp4"),
        "speakers": ("names.vtt", b"\xff", "text/vtt"),
    })
    assert response.status_code == 200
    assert seen == [None]


def test_service_fails_closed_when_no_key_is_configured(client, monkeypatch):
    monkeypatch.delenv("AI_ENGINE_API_KEY")
    assert upload(client, {"X-Api-Key": ""}).status_code == 503


def test_oversized_upload_is_rejected(client):
    assert upload(client, {"X-Api-Key": KEY}, size=2 * 1024 * 1024).status_code == 413


def test_client_file_name_never_reaches_the_file_system(client, seen_paths):
    res = upload(client, {"X-Api-Key": KEY}, name="../../api_server.py")
    assert res.status_code == 200
    used = pathlib.Path(seen_paths[0])
    assert ".." not in used.parts and "api_server" not in used.name
    assert used.suffix == ".bin"  # only audio extensions survive; anything else is neutralised
    assert not used.exists(), "temporary file must be removed after processing"


def test_internal_errors_are_not_leaked(client, monkeypatch):
    def boom(path):
        raise RuntimeError("db parolasi: hunter2")

    monkeypatch.setattr(api_server, "transcribe_audio", boom)
    res = upload(client, {"X-Api-Key": KEY})
    assert res.status_code == 500
    assert "hunter2" not in res.text


def test_llm_failure_is_reported_as_failure_not_as_an_empty_summary(monkeypatch):
    # Before: an exception produced a placeholder summary, the API said success and the meeting showed "Completed".
    # Now a provider error propagates; the endpoint answers success=false (see the transcript test below).
    import providers.groq_summarizer as gs

    class Broken:
        def chat_json(self, *args, **kwargs):
            raise gs.GroqError("down")

    with pytest.raises(gs.GroqError):
        gs.GroqSummarizer(Broken()).summarize("metin")


def test_summary_failure_still_returns_the_transcript(client, monkeypatch):
    # The transcript is kept by the backend, so a retry only has to redo the summary.
    def broken_summary(text):
        raise RuntimeError("llm down")

    monkeypatch.setattr(api_server, "generate_meeting_summary", broken_summary)
    res = upload(client, {"X-Api-Key": KEY})
    assert res.status_code == 200
    assert res.json()["success"] is False
    assert res.json()["transkript"] == "metin"


def test_summarize_endpoint_requires_the_key_and_returns_a_summary(client):
    assert client.post("/api/summarize", json={"transkript": "metin"}).status_code == 401
    res = client.post("/api/summarize", headers={"X-Api-Key": KEY}, json={"transkript": "metin"})
    assert res.status_code == 200
    assert res.json() == {"success": True, "summary": SUMMARY}


@pytest.mark.parametrize("transcript", [
    "Bucky Barnes: Altyazı M.K. İzlediğiniz için teşekkürler!",
    "Bucky Barnes: Altyazı M.K.\nAli Veli: İzlediğiniz için teşekkürler!",
])
def test_no_speech_with_zoom_speaker_names(client, monkeypatch, transcript):
    monkeypatch.setattr(api_server, "transcribe_audio", lambda path, speakers_vtt=None: transcript)
    monkeypatch.setattr(api_server, "generate_meeting_summary", lambda text: pytest.fail("silence must not be summarized"))

    response = client.post(URL, headers={"X-Api-Key": KEY}, files={
        "file": ("audio.m4a", b"audio", "audio/mp4"),
        "speakers": ("names.vtt", "WEBVTT\n\n00:00.000 --> 00:02.000\nBucky Barnes: merhaba\n\n00:02.000 --> 00:04.000\nAli Veli: evet".encode(), "text/vtt"),
    })

    assert response.status_code == 422
    assert response.json() == {"detail": "no_speech"}


def test_speaker_named_like_a_caption_still_keeps_real_speech(client, monkeypatch):
    monkeypatch.setattr(api_server, "transcribe_audio", lambda path, speakers_vtt=None: "Bucky Barnes: Altyazı M.K. Bütçeyi konuşalım.")

    response = client.post(URL, headers={"X-Api-Key": KEY}, files={
        "file": ("audio.m4a", b"audio", "audio/mp4"),
        "speakers": ("names.vtt", "WEBVTT\n\n00:00.000 --> 00:02.000\nBucky Barnes: merhaba".encode(), "text/vtt"),
    })

    assert response.status_code == 200
