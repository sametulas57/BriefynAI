"""Azure upload transcription and speaker labels, without network or ffmpeg."""

import json
import pathlib
import sys
from email import policy
from email.parser import BytesParser

import httpx
import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import api_server
from providers import azure_transcriber, build_transcriber
from providers.azure_transcriber import AzureSpeechError, AzureTranscriber

ENDPOINT = "https://speech.example.test/"
KEY = "test-azure-key"


@pytest.fixture
def source(monkeypatch, tmp_path):
    path = tmp_path / "input.wav"
    path.write_bytes(b"audio")
    monkeypatch.setattr(
        azure_transcriber.audio,
        "to_mono_flac",
        lambda src, dst: (dst.write_bytes(b"mono"), dst)[1],
    )
    monkeypatch.setattr(azure_transcriber.audio, "duration_seconds", lambda path: 60)
    return str(path)


def transcriber_with(handler, sleeps=None, **kwargs):
    sleeps = [] if sleeps is None else sleeps
    return AzureTranscriber(
        ENDPOINT,
        KEY,
        http=httpx.Client(transport=httpx.MockTransport(handler)),
        sleep=sleeps.append,
        **kwargs,
    )


def test_request_uses_azure_fast_transcription_multipart(source):
    requests = []

    def handler(request):
        requests.append(request)
        return httpx.Response(200, json={"phrases": []})

    assert transcriber_with(handler).transcribe(source) == ""
    request = requests[0]
    assert request.url.path == "/speechtotext/transcriptions:transcribe"
    assert dict(request.url.params) == {"api-version": "2025-10-15"}
    assert request.headers["Ocp-Apim-Subscription-Key"] == KEY
    message = BytesParser(policy=policy.default).parsebytes(
        f"Content-Type: {request.headers['content-type']}\r\n\r\n".encode()
        + request.content
    )
    parts = {
        part.get_param("name", header="content-disposition"): part
        for part in message.iter_parts()
    }
    assert parts["audio"].get_filename() == "mono.flac"
    assert parts["audio"].get_content_type() == "audio/flac"
    assert parts["audio"].get_payload(decode=True) == b"mono"
    definition = json.loads(parts["definition"].get_payload(decode=True))
    assert definition["locales"] == ["tr-TR"]
    assert definition["diarization"] == {"enabled": True, "maxSpeakers": 10}


def test_labels_follow_first_appearance_and_merge_consecutive_phrases(source):
    phrases = [
        {"offsetMilliseconds": 3000, "speaker": 2, "text": "D."},
        {"offsetMilliseconds": 1000, "speaker": 2, "text": " B. "},
        {"offsetMilliseconds": 500, "speaker": 9, "text": "  "},
        {"offsetMilliseconds": 2000, "speaker": 1, "text": "C."},
        {"offsetMilliseconds": 0, "speaker": 2, "text": "A."},
    ]
    transcriber = transcriber_with(
        lambda request: httpx.Response(200, json={"phrases": phrases})
    )
    assert transcriber.transcribe(source) == (
        "Konuşmacı 1: A. B.\nKonuşmacı 2: C.\nKonuşmacı 1: D."
    )


@pytest.mark.parametrize("speaker", [None, 0, 2])
def test_missing_or_single_speaker_returns_plain_text(source, speaker):
    phrases = [
        {"offsetMilliseconds": 1000, "text": " B. "},
        {"offsetMilliseconds": 0, "text": " A. "},
    ]
    if speaker is not None:
        for phrase in phrases:
            phrase["speaker"] = speaker
    transcriber = transcriber_with(
        lambda request: httpx.Response(200, json={"phrases": phrases})
    )
    assert transcriber.transcribe(source) == "A. B."


@pytest.mark.parametrize("phrases", [None, []])
def test_combined_phrases_are_used_when_phrases_are_missing_or_empty(source, phrases):
    payload = {"combinedPhrases": [{"text": " A. "}, {"text": " "}, {"text": "B."}]}
    if phrases is not None:
        payload["phrases"] = phrases
    transcriber = transcriber_with(lambda request: httpx.Response(200, json=payload))
    assert transcriber.transcribe(source) == "A. B."


@pytest.mark.parametrize(
    "retry_after, expected_wait", [("7", 7.0), ("120", 60.0), (None, 10.0)]
)
def test_rate_limit_retries_with_the_same_audio(source, retry_after, expected_wait):
    requests, sleeps = [], []

    def handler(request):
        requests.append(request)
        if len(requests) == 1:
            headers = {"retry-after": retry_after} if retry_after else {}
            return httpx.Response(429, headers=headers)
        return httpx.Response(200, json={"combinedPhrases": [{"text": "A."}]})

    assert transcriber_with(handler, sleeps).transcribe(source) == "A."
    assert sleeps == [expected_wait]
    assert len(requests) == 2
    assert all(b"mono\r\n" in request.content for request in requests)


def test_server_errors_back_off_then_fail_without_leaking_the_key(source):
    requests, sleeps = [], []

    def handler(request):
        requests.append(request)
        return httpx.Response(500, text=KEY)

    with pytest.raises(AzureSpeechError, match="HTTP 500") as error:
        transcriber_with(handler, sleeps).transcribe(source)
    assert KEY not in str(error.value)
    assert len(requests) == 5
    assert sleeps == [2.0, 4.0, 6.0, 8.0]


def test_final_rate_limit_raises_after_max_attempts(source):
    requests, sleeps = [], []

    def handler(request):
        requests.append(request)
        return httpx.Response(429, headers={"retry-after": "7"})

    with pytest.raises(AzureSpeechError, match="HTTP 429"):
        transcriber_with(handler, sleeps, max_attempts=2).transcribe(source)
    assert len(requests) == 2
    assert sleeps == [7.0, 7.0]


def test_non_retryable_error_fails_immediately_without_leaking_body(source):
    requests, sleeps = [], []

    def handler(request):
        requests.append(request)
        return httpx.Response(401, text=KEY)

    with pytest.raises(AzureSpeechError, match="^transcription failed: HTTP 401$"):
        transcriber_with(handler, sleeps).transcribe(source)
    assert len(requests) == 1
    assert sleeps == []


@pytest.mark.parametrize("duration", [5 * 3600, 5 * 3600 + 1])
def test_five_hour_limit_prevents_http_requests(source, monkeypatch, duration):
    monkeypatch.setattr(
        azure_transcriber.audio, "duration_seconds", lambda path: duration
    )
    transcriber = transcriber_with(
        lambda request: pytest.fail("no HTTP request expected")
    )
    with pytest.raises(
        AzureSpeechError, match="recording is longer than Azure's 5 hour limit"
    ):
        transcriber.transcribe(source)


@pytest.mark.parametrize("missing", ["AZURE_SPEECH_KEY", "AZURE_SPEECH_ENDPOINT"])
def test_build_azure_requires_key_and_endpoint(monkeypatch, missing):
    monkeypatch.setenv("TRANSCRIBER", "azure")
    monkeypatch.setenv("AZURE_SPEECH_KEY", KEY)
    monkeypatch.setenv("AZURE_SPEECH_ENDPOINT", ENDPOINT)
    monkeypatch.delenv(missing)
    with pytest.raises(RuntimeError, match=f"^{missing} is not set$"):
        build_transcriber()


def test_build_azure_from_environment(monkeypatch):
    monkeypatch.setenv("TRANSCRIBER", "azure")
    monkeypatch.setenv("AZURE_SPEECH_KEY", KEY)
    monkeypatch.setenv("AZURE_SPEECH_ENDPOINT", ENDPOINT)
    monkeypatch.delenv("AZURE_SPEECH_MAX_SPEAKERS", raising=False)
    assert isinstance(build_transcriber(), AzureTranscriber)


@pytest.mark.parametrize(
    "vtt, expected_kind",
    [("WEBVTT\n\nAli: söz", "groq"), (None, None)],
)
def test_api_keeps_zoom_on_groq_and_uses_configured_provider_for_uploads(
    monkeypatch, vtt, expected_kind
):
    monkeypatch.setenv("TRANSCRIBER", "azure")
    kinds, inputs = [], []

    class FakeTranscriber:
        def transcribe(self, path, speakers_vtt=None):
            inputs.append((path, speakers_vtt))
            return "metin"

    def build(kind=None):
        kinds.append(kind)
        return FakeTranscriber()

    monkeypatch.setattr(api_server, "build_transcriber", build)
    assert api_server.transcribe_audio("recording.wav", vtt) == "metin"
    assert kinds == [expected_kind]
    assert inputs == [("recording.wav", vtt)]
