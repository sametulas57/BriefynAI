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
    import summarize_text

    def broken_chat(**kwargs):
        raise RuntimeError("ollama down")

    monkeypatch.setattr(summarize_text.ollama, "chat", broken_chat)
    assert summarize_text.generate_meeting_summary("metin") is None
