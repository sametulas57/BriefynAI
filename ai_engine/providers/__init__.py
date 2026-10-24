"""AI providers behind small interfaces, so swapping a vendor touches one module (modular architecture).

TRANSCRIBER / SUMMARIZER / ANSWERER environment variables choose the implementation; today only "groq" exists.
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


def build_transcriber():
    kind = os.getenv("TRANSCRIBER", "groq")
    if kind == "groq":
        from .groq_transcriber import GroqTranscriber
        return GroqTranscriber(_groq_client(), model=os.getenv("AI_TRANSCRIBE_MODEL", TRANSCRIBE_MODEL_DEFAULT))
    raise RuntimeError(f"Unknown TRANSCRIBER: {kind}")


def build_summarizer():
    from .groq_summarizer import Summarizer
    from .llm import build_llm
    return Summarizer(build_llm("summary"))
