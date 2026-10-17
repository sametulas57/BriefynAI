"""AI providers behind small interfaces, so swapping a vendor touches one module (modular architecture).

TRANSCRIBER / SUMMARIZER environment variables choose the implementation; today only "groq" exists.
"""
import os

from .groq_client import GroqClient

TRANSCRIBE_MODEL_DEFAULT = "whisper-large-v3"
SUMMARY_MODEL_DEFAULT = "qwen/qwen3.8-27b"


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
    kind = os.getenv("SUMMARIZER", "groq")
    if kind == "groq":
        from .groq_summarizer import GroqSummarizer
        return GroqSummarizer(_groq_client(), model=os.getenv("AI_SUMMARY_MODEL", SUMMARY_MODEL_DEFAULT))
    raise RuntimeError(f"Unknown SUMMARIZER: {kind}")
