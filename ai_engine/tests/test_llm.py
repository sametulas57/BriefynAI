"""ADR-0014: one LlmClient interface; each task picks its provider and model from the environment."""
import json
import pathlib
import sys

import httpx
import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import providers  # noqa: E402
from providers.groq_client import GroqClient  # noqa: E402
from providers.groq_summarizer import SCHEMA, SYSTEM, Summarizer  # noqa: E402
from providers.llm import GroqLlm, build_llm  # noqa: E402

SUMMARY = {"toplanti_konusu": "Lansman", "gorusulen_konular": [], "alinan_kararlar": ["Release salıya"],
           "bekleyen_konular": [], "aksiyon_maddeleri": [{"kim": "Deniz", "ne": "hotfix çıkar", "ne_zaman": None}]}


class FakeLlm:
    """Stands in for any provider: the summariser must not know which one it talks to."""
    def __init__(self, reply):
        self.reply, self.calls = reply, []

    def complete_json(self, system, messages, schema):
        self.calls.append((system, messages, schema))
        return self.reply


def test_summarizer_runs_on_any_llm_client():
    llm = FakeLlm(SUMMARY)
    result = Summarizer(llm).summarize("Kısa toplantı.")

    assert result["aksiyon_maddeleri"] == ["Deniz: hotfix çıkar"]
    system, messages, schema = llm.calls[0]
    assert system == SYSTEM and schema == SCHEMA
    assert messages == [{"role": "user", "content": "Toplantı dökümü:\n\nKısa toplantı."}]


def test_groq_llm_puts_the_system_text_first_and_uses_its_model():
    bodies = []

    def handler(request):
        bodies.append(json.loads(request.content))
        return httpx.Response(200, json={"choices": [{"message": {"content": "{\"ok\": true}"}}]})

    client = GroqClient("k", http=httpx.Client(transport=httpx.MockTransport(handler)), sleep=lambda s: None)
    result = GroqLlm(client, "openai/gpt-oss-120b").complete_json("sistem", [{"role": "user", "content": "soru"}], {"type": "object"})

    assert result == {"ok": True}
    assert bodies[0]["model"] == "openai/gpt-oss-120b"
    assert bodies[0]["messages"] == [{"role": "system", "content": "sistem"}, {"role": "user", "content": "soru"}]


def test_each_task_reads_its_own_provider_and_model(monkeypatch):
    monkeypatch.setenv("GROQ_API_KEY", "k")
    monkeypatch.setenv("AI_SUMMARY_MODEL", "ozet-modeli")
    monkeypatch.setenv("AI_ANSWER_MODEL", "cevap-modeli")

    assert build_llm("summary").model == "ozet-modeli"
    assert build_llm("answer").model == "cevap-modeli"


def test_answer_model_defaults_to_gpt_oss_120b(monkeypatch):
    monkeypatch.setenv("GROQ_API_KEY", "k")
    monkeypatch.delenv("AI_ANSWER_MODEL", raising=False)
    monkeypatch.delenv("ANSWERER", raising=False)
    assert build_llm("answer").model == "openai/gpt-oss-120b"


def test_unknown_provider_or_task_fails_clearly(monkeypatch):
    monkeypatch.setenv("GROQ_API_KEY", "k")
    monkeypatch.setenv("ANSWERER", "bilinmeyen")
    with pytest.raises(RuntimeError, match="ANSWERER"):
        build_llm("answer")
    with pytest.raises(RuntimeError):
        build_llm("yok")


def test_summarizer_builder_goes_through_the_llm_layer(monkeypatch):
    monkeypatch.setenv("GROQ_API_KEY", "k")
    monkeypatch.setenv("SUMMARIZER", "groq")
    monkeypatch.setenv("AI_SUMMARY_MODEL", "ozet-modeli")
    summarizer = providers.build_summarizer()
    assert isinstance(summarizer, Summarizer)
    assert summarizer._llm.model == "ozet-modeli"
