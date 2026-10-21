import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from providers.speakers import Cue, assign_speakers, format_lines, parse_vtt
from providers.groq_transcriber import GroqTranscriber


VTT = """WEBVTT

1
00:00:16.239 --> 00:00:27.079
Ulaş Karasu: İlk cümle
devamı

2
00:27.100 --> 00:31.000
Adsız söz

broken
not a timestamp
Deniz: atla

3
00:00:32.000 --> 00:00:35.000
erdfg grgdrg: İkinci söz
"""


def test_parse_zoom_vtt_skips_bad_blocks_and_reads_names_and_multiline_text():
    assert parse_vtt(VTT) == [
        Cue(16.239, 27.079, "Ulaş Karasu", "İlk cümle devamı"),
        Cue(27.1, 31.0, None, "Adsız söz"),
        Cue(32.0, 35.0, "erdfg grgdrg", "İkinci söz"),
    ]


def test_assign_speakers_sums_overlap_by_name_and_leaves_gaps_unnamed():
    cues = [Cue(0, 2, "Ali", ""), Cue(2, 4, "Ali", ""), Cue(1, 4.5, "Ayşe", "")]
    assert assign_speakers([(0, 4, "bir"), (4, 4.5, "iki"), (5, 6, "üç")], cues) == [
        ("Ali", "bir"), ("Ayşe", "iki"), (None, "üç")]
    assert format_lines([("Ali", "bir"), ("Ali", "iki"), (None, "üç")]) == "Ali: bir iki\nüç"


def test_chunk_offsets_are_added_before_matching(monkeypatch, tmp_path):
    from providers import groq_transcriber as module
    monkeypatch.setattr(module.audio, "to_mono_flac", lambda source, target: target)
    monkeypatch.setattr(module.audio, "duration_seconds", lambda path: 120)
    monkeypatch.setattr(module.audio, "silence_midpoints", lambda path: [])
    monkeypatch.setattr(module.audio, "plan_cuts", lambda *args: [0, 60, 120])
    monkeypatch.setattr(module.audio, "cut", lambda source, start, end, target: target)

    class Client:
        def __init__(self):
            self.calls = []

        def transcribe_segments(self, *args):
            self.calls.append(args)
            return [(1, 3, "Merhaba")]

        def transcribe(self, *args):
            self.calls.append(args)
            return "Merhaba"

    client = Client()
    transcriber = GroqTranscriber(client, "test")
    vtt = "WEBVTT\n\n1\n00:00:01.000 --> 00:00:03.000\nAli: bir\n\n2\n00:01:01.000 --> 00:01:03.000\nAyşe: iki"
    assert transcriber.transcribe(str(tmp_path / "audio"), vtt) == "Ali: Merhaba\nAyşe: Merhaba"
    assert len(client.calls) == 2
    client.calls.clear()
    assert transcriber.transcribe(str(tmp_path / "audio")) == "Merhaba Merhaba"
    assert transcriber.transcribe(str(tmp_path / "audio"), "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nadsız") == "Merhaba Merhaba"
