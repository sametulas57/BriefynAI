#!/bin/sh
# Tek doğrulama komutu: C# ve Python lint/biçim, tip kontrolü, .NET build + testler ve pytest.
# Pre-commit ve CI bu komutu çalıştırır; sonuç tek satır GEÇTİ ya da KALDI olarak yazılır.
set -u
cd "$(git rev-parse --show-toplevel)" || exit 1
if [ ! -x ai_engine/.venv/bin/python ]; then
  echo "Doğrulama: KALDI (ai_engine/.venv yok)"
  echo "  Bir kez kur: python3 -m venv ai_engine/.venv && ai_engine/.venv/bin/python -m pip install -r ai_engine/requirements.txt -r ai_engine/requirements-dev.txt"
  exit 1
fi
LOG=$(mktemp)
trap 'rm -f "$LOG"' EXIT
fail() {
  echo "Doğrulama: KALDI ($1)"
  echo "--- son satırlar ---"
  tail -25 "$LOG"
  exit 1
}
gate() {
  AD="$1"
  shift
  "$@" >"$LOG" 2>&1 || fail "$AD"
}
# Bayat Razor derleme sunucusu değişmemiş sayfalarda sahte hata veriyordu (2026-10-22'de iki kez).
dotnet build-server shutdown >/dev/null 2>&1
gate "C# lint/biçim" dotnet format Briefyn.slnx --verify-no-changes
gate "Python lint" ai_engine/.venv/bin/python -m ruff check ai_engine
gate "Python biçim" ai_engine/.venv/bin/python -m ruff format --check ai_engine
gate "Python tipler" ai_engine/.venv/bin/python -m mypy ai_engine
gate ".NET build/test" dotnet test MeetingWeb.Tests --nologo
NET=$(grep -E "Toplam:|Total:" "$LOG" | tail -1)
NET_KOD=$(grep -cE "Başarısız!|Failed!|error [A-Z]+[0-9]+" "$LOG")
if [ -z "$NET" ] || [ "$NET_KOD" -ne 0 ]; then
  fail ".NET build/test"
fi
gate "pytest" ai_engine/.venv/bin/python -m pytest ai_engine/tests -q
PY=$(grep -E "passed|failed|error" "$LOG" | tail -1)
PY_KOD=$(printf '%s' "$PY" | grep -cE '[0-9]+ (failed|errors?)' || true)
if [ -z "$PY" ] || [ "$PY_KOD" -ne 0 ]; then
  fail "pytest"
fi
GECEN=$(printf '%s' "$NET" | sed -nE 's/.*(Başarılı|Passed): *([0-9]+).*/\2/p')
TOPLAM=$(printf '%s' "$NET" | sed -nE 's/.*(Toplam|Total): *([0-9]+).*/\2/p')
echo "Doğrulama: GEÇTİ (lint, biçim, tipler · .NET $GECEN/$TOPLAM · pytest $(printf '%s' "$PY" | sed -E 's/ in [0-9.]+s//'))"
