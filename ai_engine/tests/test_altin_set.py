"""Deneme soru seti (altın set) kendi içinde tutarlı mı: ölçüm ancak veri doğruysa anlamlı (PRD §7)."""
import json
import pathlib
import re
from collections import Counter

ALTIN = pathlib.Path(__file__).resolve().parent / "altin"
DATA = json.loads((ALTIN / "toplantilar.json").read_text(encoding="utf-8"))
QUESTIONS = json.loads((ALTIN / "sorular.json").read_text(encoding="utf-8"))
MEETINGS = {m["id"]: m for m in DATA["toplantilar"]}
SERIES = {s["id"]: s for s in DATA["seriler"]}
TEAMS = {t["id"] for t in DATA["ekipler"]}
UNANSWERED = {"cevapsiz", "kapsam", "sizma"}
TYPES = UNANSWERED | {"olgu", "karar", "gecersiz_kilma", "karar_yok", "kisi", "zaman", "seri", "toplanti", "talimat"}


def norm(text: str) -> str:
    text = text.replace("İ", "i").replace("I", "ı").lower()
    return re.sub(r"\s+", " ", text).strip()


def meeting_text(m: dict) -> str:
    parts = [m["baslik"], *m["dokum"], m["ozet"]["toplanti_konusu"], *m["ozet"]["konular"],
             *m["ozet"]["kararlar"], *m["ozet"]["bekleyen"], *(a["metin"] for a in m["ozet"]["aksiyonlar"])]
    return norm("\n".join(parts))


def in_scope(q: dict) -> set[str]:
    scope = q["kapsam"]
    team = [mid for mid, m in MEETINGS.items() if m["ekip"] == q["ekip"]]
    if scope["tur"] == "seri":
        return {mid for mid in team if MEETINGS[mid]["seri"] == scope["hedef"]}
    if scope["tur"] == "toplanti":
        return {scope["hedef"]}
    return set(team)


def test_meetings_are_well_formed():
    assert 10 <= len(MEETINGS) <= 14 and len(MEETINGS) == len(DATA["toplantilar"])
    assert Counter(m["ekip"] for m in MEETINGS.values()).keys() == TEAMS == {"A", "B"}
    for m in MEETINGS.values():
        assert m["seri"] is None or SERIES[m["seri"]]["ekip"] == m["ekip"]
        assert all(re.match(r"^[^:]{3,40}: \S", line) for line in m["dokum"]), m["id"]
        for action in m["ozet"]["aksiyonlar"]:
            assert set(action) == {"metin", "sorumlu", "son_tarih", "bitti"}
    assert Counter(m["seri"] for m in MEETINGS.values() if m["seri"])["S1"] == 4


def test_two_transcripts_carry_a_hidden_instruction():
    hidden = [mid for mid, m in MEETINGS.items() if "yapay zekâ" in meeting_text(m) or "asistan" in meeting_text(m)]
    assert sorted(hidden) == ["A7", "B3"]
    for mid in hidden:  # the stored summary ignored it, like a well-behaved model would
        assert "asistan" not in norm(json.dumps(MEETINGS[mid]["ozet"], ensure_ascii=False))


def test_questions_are_well_formed_and_balanced():
    assert 40 <= len(QUESTIONS) <= 60
    assert len({q["id"] for q in QUESTIONS}) == len(QUESTIONS)
    kinds = Counter(q["tur"] for q in QUESTIONS)
    assert set(kinds) <= TYPES
    assert sum(n for k, n in kinds.items() if k not in UNANSWERED) >= 30
    assert kinds["cevapsiz"] + kinds["kapsam"] >= 10
    assert kinds["talimat"] >= 5 and kinds["sizma"] >= 5
    for q in QUESTIONS:
        assert q["ekip"] in TEAMS and len(q["soru"]) <= 500
        scope, target = q["kapsam"]["tur"], q["kapsam"]["hedef"]
        owner = {"arsiv": lambda: q["ekip"], "seri": lambda: SERIES[target]["ekip"],
                 "toplanti": lambda: MEETINGS[target]["ekip"]}[scope]
        assert (target is None) == (scope == "arsiv") and owner() == q["ekip"], q["id"]


def test_expected_evidence_is_in_the_asking_teams_meeting_and_in_scope():
    for q in QUESTIONS:
        if q["tur"] in UNANSWERED:
            assert q["beklenen"] == [] and q["cevap_ozu"] is None, q["id"]
            continue
        assert q["beklenen"] and q["cevap_ozu"], q["id"]
        for hit in q["beklenen"]:
            assert hit["toplanti"] in in_scope(q), q["id"]
            assert norm(hit["kanit"]) in meeting_text(MEETINGS[hit["toplanti"]]), (q["id"], hit["kanit"])


def test_injection_questions_hit_the_poisoned_meetings_and_name_what_must_not_leak():
    for q in (q for q in QUESTIONS if q["tur"] == "talimat"):
        assert {h["toplanti"] for h in q["beklenen"]} & {"A7", "B3"}, q["id"]
        assert q["yasak"], q["id"]


def test_leak_questions_ask_about_the_other_teams_data():
    for q in (q for q in QUESTIONS if q["tur"] == "sizma"):
        other = "\n".join(meeting_text(m) for m in MEETINGS.values() if m["ekip"] != q["ekip"])
        assert norm(q["iz"]) in other, q["id"]
        assert all(norm(q["iz"]) not in meeting_text(m) for m in MEETINGS.values() if m["ekip"] == q["ekip"]), q["id"]
