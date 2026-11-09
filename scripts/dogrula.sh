#!/bin/sh
# Tek doğrulama komutu (2026-10-19): .NET build + testler ve ai_engine pytest.
# Sonuç tek satır: GEÇTİ ya da KALDI. "Bitti" demeden önce bu komut geçmeli (AGENTS.md).
# Geçince çalışma ağacının durumunu damgalar; Claude'un Stop hook'u (scripts/dogrula_stop_hook.py)
# değişmiş kodun doğrulanıp doğrulanmadığını bu damgayla anlar. Pre-commit de bu komutu çalıştırır.
set -u
cd "$(git rev-parse --show-toplevel)" || exit 1
BAS=$(scripts/dogrula_stop_hook.py --hash)
if [ -z "$BAS" ]; then
  echo "Doğrulama: KALDI (çalışma ağacının parmak izi alınamadı)"
  exit 1
fi
if [ ! -x ai_engine/.venv/bin/python ]; then
  echo "Doğrulama: KALDI (ai_engine/.venv yok; kurulum AGENTS.md 'Çalıştırma ve test')"
  exit 1
fi
LOG=$(mktemp)
# Bayat Razor derleme sunucusu değişmemiş sayfalarda sahte hata veriyordu (2026-10-22'de iki kez).
dotnet build-server shutdown >/dev/null 2>&1
NET=$(dotnet test MeetingWeb.Tests --nologo 2>&1 | tee "$LOG" | grep -E "Toplam:|Total:" | tail -1)
NET_KOD=$(grep -cE "Başarısız!|Failed!|error [A-Z]+[0-9]+" "$LOG")
PY=$(ai_engine/.venv/bin/python -m pytest ai_engine/tests -q 2>&1 | tee -a "$LOG" | grep -E "passed|failed|error" | tail -1)
PY_KOD=$(printf '%s' "$PY" | grep -cE '[0-9]+ (failed|errors?)' || true)
if [ -z "$NET" ] || [ -z "$PY" ] || [ "$NET_KOD" -ne 0 ] || [ "$PY_KOD" -ne 0 ]; then
  echo "Doğrulama: KALDI"
  echo "  .NET: ${NET:-sonuç yok}"
  echo "  pytest: ${PY:-sonuç yok}"
  echo "--- son satırlar ---"
  grep -E "Başarısız|Failed|error|Error|FAILED|assert" "$LOG" | tail -25
  rm -f "$LOG"
  exit 1
fi
rm -f "$LOG"
GECEN=$(printf '%s' "$NET" | sed -nE 's/.*(Başarılı|Passed): *([0-9]+).*/\2/p')
TOPLAM=$(printf '%s' "$NET" | sed -nE 's/.*(Toplam|Total): *([0-9]+).*/\2/p')
scripts/dogrula_stop_hook.py --damgala "$BAS" || exit 1
echo "Doğrulama: GEÇTİ (.NET $GECEN/$TOPLAM · pytest $(printf '%s' "$PY" | sed -E 's/ in [0-9.]+s//'))"
