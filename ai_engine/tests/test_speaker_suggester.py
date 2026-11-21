"""Speaker name proposals must be grounded in the transcript and remain optional."""

import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import providers
from providers.llm import build_llm
from providers.speaker_suggester import SCHEMA, SYSTEM, SpeakerSuggester, labels

TRANSCRIPT = (
    "Konuşmacı 1: Herkese günaydın. Murat, tedarik ne durumda?\n"
    "Konuşmacı 2: Gecikme var, sözleşmeyi bugün imzalarım."
)


class FakeLlm:
    def __init__(self, reply):
        self.reply = reply
        self.calls = []

    def complete_json(self, system, messages, schema):
        self.calls.append((system, messages, schema))
        return self.reply


def test_labels_are_distinct_and_in_first_appearance_order():
    assert labels(
        "Konuşmacı 2: Merhaba.\nKonuşmacı 1: Evet.\nKonuşmacı 2: Tamam.\n"
        "Satır içinde Konuşmacı 3: söz\nKonuşmacı 4:söz"
    ) == ["Konuşmacı 2", "Konuşmacı 1"]


@pytest.mark.parametrize("transcript", ["", "Merhaba.", "Konuşmacı 1: Merhaba."])
def test_fewer_than_two_labels_never_calls_llm(transcript):
    llm = FakeLlm({"oneriler": []})
    assert SpeakerSuggester(llm).suggest(transcript) == {}
    assert llm.calls == []


def test_valid_proposal_keeps_name_and_reason_and_uses_json_schema():
    reason = "Murat'a hitap edildikten hemen sonra cevap veriyor."
    llm = FakeLlm(
        {"oneriler": [{"konusmaci": "Konuşmacı 2", "ad": " Murat ", "gerekce": reason}]}
    )

    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {
        "Konuşmacı 2": {"ad": "Murat", "gerekce": reason}
    }
    assert llm.calls == [
        (
            SYSTEM,
            [
                {
                    "role": "user",
                    "content": "Konuşmacılar: Konuşmacı 1, Konuşmacı 2\n\nToplantı dökümü:\n\n"
                    + TRANSCRIPT,
                }
            ],
            SCHEMA,
        )
    ]


@pytest.mark.parametrize(
    "name",
    [
        None,
        "",
        "  ",
        "Elif",
        "Mura",
        "Murat Bey",
        "Murat:",
        "Konuşmacı 3",
        "Murat\n",
        "Murat\x00",
        "Murat\x7f",
        "Murat\u200b",
        "Murat " * 11,
        42,
    ],
)
def test_invalid_names_are_dropped(name):
    llm = FakeLlm(
        {"oneriler": [{"konusmaci": "Konuşmacı 2", "ad": name, "gerekce": "ipucu"}]}
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {}


def test_existing_speaker_label_is_not_a_name_proposal():
    llm = FakeLlm(
        {
            "oneriler": [
                {"konusmaci": "Konuşmacı 1", "ad": "Konuşmacı 2", "gerekce": "ipucu"}
            ]
        }
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {}


def test_unknown_speaker_label_is_dropped():
    llm = FakeLlm(
        {"oneriler": [{"konusmaci": "Konuşmacı 9", "ad": "Murat", "gerekce": "ipucu"}]}
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {}


def test_label_without_turkish_letters_is_matched_by_number():
    # Seen live (2026-11-20): qwen answered "Konusmaci 2" and the string "null".
    llm = FakeLlm(
        {
            "oneriler": [
                {"konusmaci": "Konusmaci 2", "ad": "Murat", "gerekce": "ipucu"},
                {"konusmaci": "Konuşmacı 1", "ad": "null", "gerekce": ""},
            ]
        }
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {
        "Konuşmacı 2": {"ad": "Murat", "gerekce": "ipucu"}
    }


def test_turkish_casing_matches_whole_words():
    llm = FakeLlm(
        {"oneriler": [{"konusmaci": "Konuşmacı 2", "ad": "İlker", "gerekce": ""}]}
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT.replace("Murat", "İLKER")) == {
        "Konuşmacı 2": {"ad": "İlker", "gerekce": ""}
    }


def test_first_valid_proposal_wins_and_reason_is_cleaned_and_limited():
    reason = " Kanıt\n\t" + "söz " * 70
    llm = FakeLlm(
        {
            "oneriler": [
                {"konusmaci": "Konuşmacı 2", "ad": "Elif", "gerekce": "uydurma"},
                {"konusmaci": "Konuşmacı 2", "ad": "Murat", "gerekce": reason},
                {"konusmaci": "Konuşmacı 2", "ad": "Murat", "gerekce": "ikinci"},
            ]
        }
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {
        "Konuşmacı 2": {"ad": "Murat", "gerekce": " ".join(reason.split())[:200]}
    }


@pytest.mark.parametrize(
    "item",
    [
        None,
        [],
        "Murat",
        {},
        {"konusmaci": []},
        {"konusmaci": "Konuşmacı 2", "ad": "Murat"},
        {"konusmaci": "Konuşmacı 2", "ad": "Murat", "gerekce": None},
    ],
)
def test_malformed_items_do_not_hide_later_valid_proposals(item):
    llm = FakeLlm(
        {"oneriler": [item, {"konusmaci": "Konuşmacı 2", "ad": "Murat", "gerekce": ""}]}
    )
    assert SpeakerSuggester(llm).suggest(TRANSCRIPT) == {
        "Konuşmacı 2": {"ad": "Murat", "gerekce": ""}
    }


@pytest.mark.parametrize("reply", [{}, {"oneriler": None}, {"oneriler": "Murat"}])
def test_malformed_proposal_list_returns_no_suggestions(reply):
    assert SpeakerSuggester(FakeLlm(reply)).suggest(TRANSCRIPT) == {}


def test_long_transcript_keeps_only_leading_whole_lines():
    text = TRANSCRIPT + "\nKonuşmacı 3: Ben Elif."
    llm = FakeLlm({"oneriler": []})
    limit = len(TRANSCRIPT) + 5
    SpeakerSuggester(llm, max_chars=limit).suggest(text)
    content = llm.calls[0][1][0]["content"]
    sent_text = content.split("Toplantı dökümü:\n\n", 1)[1]
    assert sent_text.rstrip("\n") == TRANSCRIPT
    assert len(sent_text) < limit
    assert content.startswith("Konuşmacılar: Konuşmacı 1, Konuşmacı 2, Konuşmacı 3\n")


def test_overlong_first_line_sends_leading_characters_instead_of_empty_text():
    transcript = "Konuşmacı 1: " + "a" * 68 + "\nKonuşmacı 2: Merhaba."
    llm = FakeLlm({"oneriler": []})
    SpeakerSuggester(llm, max_chars=50).suggest(transcript)
    sent_text = llm.calls[0][1][0]["content"].split("Toplantı dökümü:\n\n", 1)[1]
    assert sent_text == transcript[:50]


def test_system_explains_that_the_addressed_person_is_not_the_speaker():
    assert "hitap edilen kişi" in SYSTEM


def test_builder_uses_speakers_task(monkeypatch):
    import providers.llm as llm_module

    fake = FakeLlm({"oneriler": []})
    tasks = []
    monkeypatch.setattr(
        llm_module, "build_llm", lambda task: tasks.append(task) or fake
    )
    assert providers.build_speaker_suggester().suggest(TRANSCRIPT) == {}
    assert tasks == ["speakers"]
    assert len(fake.calls) == 1


@pytest.mark.parametrize("setting", [None, "", "custom-model"])
def test_speakers_task_uses_its_own_model_and_defaults(monkeypatch, setting):
    monkeypatch.setenv("GROQ_API_KEY", "test")
    monkeypatch.setenv("SPEAKER_SUGGESTER", "")
    monkeypatch.setenv("AI_SUMMARY_MODEL", "other-task-model")
    if setting is None:
        monkeypatch.delenv("AI_SPEAKERS_MODEL", raising=False)
    else:
        monkeypatch.setenv("AI_SPEAKERS_MODEL", setting)
    assert build_llm("speakers").model == (setting or "openai/gpt-oss-120b")


def test_speakers_task_rejects_unknown_provider(monkeypatch):
    monkeypatch.setenv("SPEAKER_SUGGESTER", "unknown")
    with pytest.raises(RuntimeError, match="SPEAKER_SUGGESTER"):
        build_llm("speakers")
