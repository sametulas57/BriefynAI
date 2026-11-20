"""AI providers behind small interfaces, so swapping a vendor touches one module (modular architecture).

TRANSCRIBER selects "groq" (default) or "azure"; SUMMARIZER / ANSWERER select chat providers.
Chat models go through llm.build_llm (ADR-0014).
"""

import os

from .groq_client import GroqClient

TRANSCRIBE_MODEL_DEFAULT = "whisper-large-v3"


def _groq_client() -> GroqClient:
    key = os.getenv("GROQ_API_KEY", "")
    if not key:
        raise RuntimeError("GROQ_API_KEY is not set")
    return GroqClient(key)


def build_transcriber(kind: str | None = None):
    kind = kind or os.getenv("TRANSCRIBER") or "groq"
    if kind == "groq":
        from .groq_transcriber import GroqTranscriber

        return GroqTranscriber(
            _groq_client(),
            model=os.getenv("AI_TRANSCRIBE_MODEL", TRANSCRIBE_MODEL_DEFAULT),
        )
    if kind == "azure":
        from .azure_transcriber import AzureTranscriber

        key = os.getenv("AZURE_SPEECH_KEY", "")
        if not key:
            raise RuntimeError("AZURE_SPEECH_KEY is not set")
        endpoint = os.getenv("AZURE_SPEECH_ENDPOINT", "")
        if not endpoint:
            raise RuntimeError("AZURE_SPEECH_ENDPOINT is not set")
        return AzureTranscriber(
            endpoint,
            key,
            max_speakers=int(os.getenv("AZURE_SPEECH_MAX_SPEAKERS", "10")),
        )
    raise RuntimeError(f"Unknown TRANSCRIBER: {kind}")


def build_summarizer():
    from .groq_summarizer import Summarizer
    from .llm import build_llm

    return Summarizer(build_llm("summary"))


def build_speaker_suggester():
    from .llm import build_llm
    from .speaker_suggester import SpeakerSuggester

    return SpeakerSuggester(build_llm("speakers"))


def build_answerer():
    from .answerer import Answerer
    from .llm import build_llm

    return Answerer(build_llm("answer"))
