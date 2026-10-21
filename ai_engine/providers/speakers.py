"""Read Zoom VTT cues and attach their speaker names to timed Groq segments."""
from dataclasses import dataclass
import re


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
