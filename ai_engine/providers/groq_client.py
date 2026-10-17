"""Thin Groq HTTP client (OpenAI-compatible API) with rate-limit handling.

Groq does not retain inference data by default; Zero Data Retention is switched on in the Groq console (Data Controls).
"""
import json
import logging
import time
from pathlib import Path
from typing import Callable

import httpx

BASE_URL = "https://api.groq.com/openai/v1"


class GroqError(RuntimeError):
    pass


class GroqClient:
    def __init__(self, api_key: str, http: httpx.Client | None = None, max_attempts: int = 5,
                 sleep: Callable[[float], None] = time.sleep):
        self._http = http or httpx.Client(timeout=300)
        self._headers = {"Authorization": f"Bearer {api_key}"}
        self._max_attempts = max_attempts
        self._sleep = sleep

    def _post(self, path: str, **kwargs) -> httpx.Response:
        # Free tier has per-minute limits: on 429 wait as long as Groq asks (capped), on 5xx back off briefly.
        last = None
        for attempt in range(1, self._max_attempts + 1):
            last = self._http.post(f"{BASE_URL}{path}", headers=self._headers, **kwargs)
            if last.status_code == 429:
                wait = min(float(last.headers.get("retry-after", "10")), 60.0)
                logging.warning(f"Groq rate limit, waiting {wait:.0f}s (attempt {attempt})")
                self._sleep(wait)
                continue
            if last.status_code >= 500 and attempt < self._max_attempts:
                self._sleep(2.0 * attempt)
                continue
            return last
        return last

    def transcribe(self, audio: Path, model: str, language: str, prompt: str) -> str:
        with audio.open("rb") as f:
            res = self._post("/audio/transcriptions",
                             data={"model": model, "language": language, "prompt": prompt,
                                   "response_format": "json", "temperature": "0"},
                             files={"file": (audio.name, f, "audio/flac")})
        if res.status_code != 200:
            raise GroqError(f"transcription failed: HTTP {res.status_code}")
        return res.json().get("text", "").strip()

    def chat_json(self, model: str, messages: list[dict], schema: dict) -> dict:
        body = {"model": model, "messages": messages, "temperature": 0.2,
                "response_format": {"type": "json_schema", "json_schema": {"name": "result", "schema": schema, "strict": True}}}
        if model.startswith("qwen/"):
            body["reasoning_format"] = "hidden"  # qwen3 thinks first; keep only the answer

        res = self._post("/chat/completions", json=body)
        if res.status_code == 400:
            # Some models do not support strict schemas: fall back to JSON mode with the schema in the prompt.
            body["response_format"] = {"type": "json_object"}
            body["messages"] = [dict(messages[0], content=messages[0]["content"] +
                                     "\nYalnız şu JSON şemasına uyan bir nesne döndür: " + json.dumps(schema, ensure_ascii=False))] + messages[1:]
            res = self._post("/chat/completions", json=body)
        if res.status_code != 200:
            raise GroqError(f"chat failed: HTTP {res.status_code}")
        try:
            return json.loads(res.json()["choices"][0]["message"]["content"])
        except (KeyError, IndexError, json.JSONDecodeError) as e:
            raise GroqError("chat returned no valid JSON") from e
