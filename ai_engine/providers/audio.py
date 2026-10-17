"""Audio helpers built on ffmpeg (installed in the Docker image)."""
import re
import subprocess
from pathlib import Path


def to_mono_flac(source: Path, target: Path) -> Path:
    # Whisper works on 16 kHz mono; stereo would also be billed per channel.
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(source), "-ac", "1", "-ar", "16000", str(target)], check=True)
    return target


def duration_seconds(audio: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(audio)],
                         capture_output=True, text=True, check=True).stdout
    return float(out.strip())


def silence_midpoints(audio: Path) -> list[float]:
    err = subprocess.run(["ffmpeg", "-i", str(audio), "-af", "silencedetect=noise=-35dB:d=0.4", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    starts = [float(x) for x in re.findall(r"silence_start: ([\d.]+)", err)]
    ends = [float(x) for x in re.findall(r"silence_end: ([\d.]+)", err)]
    return [(s + e) / 2 for s, e in zip(starts, ends)]


def plan_cuts(duration: float, silences: list[float], target: float = 60.0) -> list[float]:
    """Cut points roughly every `target` seconds, moved to the nearest silence so no word is split."""
    cuts, mark = [0.0], target
    while mark < duration - target / 3:
        nearest = min(silences, key=lambda s: abs(s - mark), default=mark)
        cut = nearest if abs(nearest - mark) <= target / 3 else mark
        if cut > cuts[-1] + 5:
            cuts.append(round(cut, 3))
        mark = cuts[-1] + target
    return cuts + [duration]


def cut(audio: Path, start: float, end: float, target: Path) -> Path:
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", str(start), "-t", str(end - start), "-i", str(audio), str(target)], check=True)
    return target
