"""Azure fast transcription adds diarization for uploaded files; Groq Whisper cannot.

Zoom recordings keep Groq transcription and the speaker names from their VTT files.
"""

import json
import tempfile
import time
from collections.abc import Callable
from pathlib import Path

import httpx

from . import audio
from .speakers import format_lines


class AzureSpeechError(RuntimeError):
    pass


class AzureTranscriber:
    def __init__(
        self,
        endpoint: str,
        api_key: str,
        locale: str = "tr-TR",
        max_speakers: int = 10,
        http: httpx.Client | None = None,
        max_attempts: int = 5,
        sleep: Callable[[float], None] = time.sleep,
    ):
        self._url = (
            f"{endpoint.rstrip('/')}/speechtotext/transcriptions:transcribe"
            "?api-version=2025-10-15"
        )
        self._headers = {"Ocp-Apim-Subscription-Key": api_key}
        self._definition = json.dumps(
            {
                "locales": [locale],
                "diarization": {"enabled": True, "maxSpeakers": max_speakers},
            }
        )
        self._http = http or httpx.Client(timeout=900)
        self._max_attempts = max_attempts
        self._sleep = sleep

    def transcribe(self, audio_path: str, speakers_vtt: str | None = None) -> str:
        # VTT is ignored: the API routes Zoom recordings to Groq for their supplied names.
        with tempfile.TemporaryDirectory(prefix="briefyn_azure_") as tmp:
            work = Path(tmp)
            mono = audio.to_mono_flac(Path(audio_path), work / "mono.flac")
            if audio.duration_seconds(mono) >= 5 * 3600:
                raise AzureSpeechError("recording is longer than Azure's 5 hour limit")
            response = self._post(mono)
        if response.status_code != 200:
            raise AzureSpeechError(f"transcription failed: HTTP {response.status_code}")
        result = response.json()
        phrases = result.get("phrases", [])
        if not phrases:
            return " ".join(
                text
                for phrase in result.get("combinedPhrases", [])
                if (text := phrase.get("text", "").strip())
            )
        pairs: list[tuple[str | None, str]] = []
        labels: dict[int, str] = {}
        for phrase in sorted(
            phrases, key=lambda phrase: phrase.get("offsetMilliseconds", 0)
        ):
            text = phrase.get("text", "").strip()
            if not text:
                continue
            speaker = phrase.get("speaker")
            if speaker is not None and speaker not in labels:
                labels[speaker] = f"Konuşmacı {len(labels) + 1}"
            pairs.append((labels.get(speaker), text))
        if len(labels) < 2:
            return " ".join(text for _, text in pairs)
        return format_lines(pairs)

    def _post(self, mono: Path) -> httpx.Response:
        last = None
        for attempt in range(1, self._max_attempts + 1):
            with mono.open("rb") as file:
                last = self._http.post(
                    self._url,
                    headers=self._headers,
                    files={"audio": ("mono.flac", file, "audio/flac")},
                    data={"definition": self._definition},
                )
            if last.status_code == 429:
                self._sleep(min(float(last.headers.get("retry-after", "10")), 60.0))
                continue
            if last.status_code >= 500 and attempt < self._max_attempts:
                self._sleep(2.0 * attempt)
                continue
            return last
        if last is None:
            raise ValueError("max_attempts must be at least 1")
        return last
