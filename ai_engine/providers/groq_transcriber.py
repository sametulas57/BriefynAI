"""Speech-to-text with Groq Whisper, sent in ~60 s pieces.

Why pieces: in our test (2026-10-16) a single 4-minute upload came back with Turkish letters dropped in one
section ("Karanl tema"); the same audio sent in silence-aligned pieces was correct. Pieces also keep each request
far below Groq's upload size limit, whatever the meeting length.
"""
import tempfile
from pathlib import Path

from . import audio
from .groq_client import GroqClient
from .speakers import assign_speakers, format_lines, parse_vtt

# Keeps English technical terms as spoken instead of translating or mangling them.
TECH_PROMPT = (
    "Bu bir yazılım toplantısı transkripti. İngilizce teknik terimleri orijinal yazımıyla koru: "
    "backend, frontend, API, endpoint, database, SQL, server, client, microservice, "
    "Docker, Kubernetes, GitHub, pull request, merge, deployment, CI/CD, bug, ticket."
)


class GroqTranscriber:
    def __init__(self, client: GroqClient, model: str, chunk_seconds: float = 60.0, language: str = "tr"):
        self._client, self._model, self._chunk, self._language = client, model, chunk_seconds, language

    def transcribe(self, audio_path: str, speakers_vtt: str | None = None) -> str:
        cues = parse_vtt(speakers_vtt) if speakers_vtt else []
        named = any(cue.speaker for cue in cues)
        with tempfile.TemporaryDirectory(prefix="briefyn_stt_") as tmp:
            work = Path(tmp)
            mono = audio.to_mono_flac(Path(audio_path), work / "mono.flac")
            cuts = audio.plan_cuts(audio.duration_seconds(mono), audio.silence_midpoints(mono), self._chunk)
            parts = []
            segments = []
            for i, (start, end) in enumerate(zip(cuts, cuts[1:])):
                piece = audio.cut(mono, start, end, work / f"part{i}.flac")
                if named:
                    segments.extend((cuts[i] + a, cuts[i] + b, text) for a, b, text in
                                    self._client.transcribe_segments(piece, self._model, self._language, TECH_PROMPT))
                else:
                    parts.append(self._client.transcribe(piece, self._model, self._language, TECH_PROMPT))
        if named:
            return format_lines(assign_speakers(segments, cues))
        return " ".join(p for p in parts if p)
