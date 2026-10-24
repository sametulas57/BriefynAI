"""One chat interface for every task (ADR-0014): system text + messages + JSON schema -> dict.

Each task reads its provider and model from the environment, so summary and answer can run on
different models; swapping a vendor means a new class here plus an environment variable.
"""
import os
from typing import Protocol

from .groq_client import GroqClient

# task -> (provider variable, model variable, default model)
TASKS = {
    "summary": ("SUMMARIZER", "AI_SUMMARY_MODEL", "qwen/qwen3.8-27b"),
    "answer": ("ANSWERER", "AI_ANSWER_MODEL", "openai/gpt-oss-20b"),  # 2026-10-24 ölçümü, ADR-0014
}


class LlmClient(Protocol):
    def complete_json(self, system: str, messages: list[dict], schema: dict) -> dict: ...


class GroqLlm:
    """Strict json_schema with json_object fallback and 429/5xx retries live in GroqClient."""
    def __init__(self, client: GroqClient, model: str):
        self._client, self.model = client, model

    def complete_json(self, system: str, messages: list[dict], schema: dict) -> dict:
        return self._client.chat_json(self.model, [{"role": "system", "content": system}, *messages], schema)


def build_llm(task: str) -> LlmClient:
    if task not in TASKS:
        raise RuntimeError(f"Unknown LLM task: {task}")
    kind_var, model_var, default_model = TASKS[task]
    kind = os.getenv(kind_var) or "groq"  # compose passes unset variables as empty strings
    if kind == "groq":
        from . import _groq_client
        return GroqLlm(_groq_client(), os.getenv(model_var) or default_model)
    raise RuntimeError(f"Unknown {kind_var}: {kind}")
