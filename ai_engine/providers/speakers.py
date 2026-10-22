"""Read Zoom VTT cues and attach their speaker names to timed Groq segments."""
from dataclasses import dataclass
import re
import unicodedata


OLD_TECH_INSTRUCTION = "İngilizce teknik terimleri orijinal yazımıyla koru"
HALLUCINATED_PHRASES = (
    "Altyazı M.K.",
    "İzlediğiniz için teşekkür ederim",
    "İzlediğiniz için teşekkürler",
    "Abone olmayı unutmayın",
)


def _normalise_text(text: str) -> str:
    # Turkish dotted/dotless I must not collapse to the wrong letter during case folding.
    text = text.translate(str.maketrans({"I": "ı", "İ": "i"})).casefold()
    text = "".join(char for char in unicodedata.normalize("NFKD", text) if not unicodedata.combining(char))
    return " ".join("".join(char if char.isalnum() else " " for char in text).split())


def is_prompt_echo(text: str, prompt: str) -> bool:
    candidate, hint = _normalise_text(text), _normalise_text(prompt)
    # A long run of the hint, matched as whole words; a few spoken terms such as "Docker, GitHub" are real speech.
    echo = len(candidate.split()) >= 6 and f" {candidate} " in f" {hint} "
    return bool(candidate and (candidate == _normalise_text(OLD_TECH_INSTRUCTION) or candidate == hint or echo))


def is_hallucinated_caption(text: str) -> bool:
    candidate = _normalise_text(text)
    if not candidate:
        return False
    # Exact matches only: a real sentence such as "Altyazı desteğini ekleyelim." must stay.
    return any(candidate == _normalise_text(phrase) for phrase in HALLUCINATED_PHRASES)


def clean_segment_text(text: str, prompt: str) -> str:
    text = text.strip()
    if is_prompt_echo(text, prompt) or is_hallucinated_caption(text):
        return ""
    return text


def clean_plain_chunk(text: str, prompt: str) -> str:
    text = text.strip()
    if not text or is_prompt_echo(text, prompt):
        return ""
    # Only remove a matching final sentence; occurrences inside a sentence are spoken content.
    # The last two pieces are tried too, because initials such as "M.K." split a phrase into two.
    spans = list(re.finditer(r"[^.!?…]+[.!?…]*", text))
    for span in reversed(spans[-2:]):
        if is_hallucinated_caption(text[span.start():]):
            return text[:span.start()].rstrip()
    return text


@dataclass(frozen=True)
class Cue:
    start: float
    end: float
    speaker: str | None
    text: str


_TIMES = re.compile(r"^(\d{2}:)?\d{2}:\d{2}\.\d{3} --> (\d{2}:)?\d{2}:\d{2}\.\d{3}(?:\s+.*)?$")


def _seconds(value: str) -> float:
    parts = value.split(":")
    if len(parts) == 2:
        minutes, seconds = parts
        return int(minutes) * 60 + float(seconds)
    hours, minutes, seconds = parts
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def parse_vtt(text: str) -> list[Cue]:
    cues = []
    for block in re.split(r"\n\s*\n", text.replace("\r\n", "\n").lstrip("\ufeff")):
        lines = [line.strip() for line in block.splitlines() if line.strip()]
        timing = next((i for i, line in enumerate(lines[:2]) if _TIMES.fullmatch(line)), None)
        if timing is None or timing + 1 >= len(lines):
            continue
        try:
            start_text, end_text = lines[timing].split(" --> ", 1)
            start, end = _seconds(start_text), _seconds(end_text.split()[0])
            if end <= start:
                continue
        except (ValueError, IndexError):
            continue
        spoken = lines[timing + 1:]
        first = spoken[0]
        speaker, first_text = first.split(": ", 1) if ": " in first else (None, first)
        cues.append(Cue(start, end, speaker or None, " ".join([first_text, *spoken[1:]]).strip()))
    return cues


def assign_speakers(segments: list[tuple[float, float, str]], cues: list[Cue]) -> list[tuple[str | None, str]]:
    pairs = []
    for start, end, text in segments:
        overlap: dict[str | None, float] = {}
        for cue in cues:
            seconds = max(0.0, min(end, cue.end) - max(start, cue.start))
            if seconds > 0:
                overlap[cue.speaker] = overlap.get(cue.speaker, 0.0) + seconds
        pairs.append((max(overlap, key=overlap.get) if overlap else None, text))
    return pairs


def format_lines(pairs: list[tuple[str | None, str]]) -> str:
    lines: list[tuple[str | None, str]] = []
    for speaker, text in pairs:
        text = text.strip()
        if not text:
            continue
        if lines and lines[-1][0] == speaker:
            previous, words = lines[-1]
            lines[-1] = (previous, words + " " + text)
        else:
            lines.append((speaker, text))
    return "\n".join(f"{speaker}: {text}" if speaker else text for speaker, text in lines)
