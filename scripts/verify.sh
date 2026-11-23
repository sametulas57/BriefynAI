#!/bin/sh
# Tek doğrulama komutu: CSS derleme, C# lint/biçim, .NET build + testler.
# Pre-commit ve CI bu komutu çalıştırır; sonuç tek satır GEÇTİ ya da KALDI olarak yazılır.
set -u
cd "$(git rev-parse --show-toplevel)" || exit 1
if [ ! -d node_modules ]; then
  echo "Doğrulama: KALDI (node_modules yok)"
  echo "  Bir kez kur: npm ci"
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
gate "CSS derleme" npm run css:build
# Bayat Razor derleme sunucusu değişmemiş sayfalarda sahte hata veriyordu (2026-10-22'de iki kez).
dotnet build-server shutdown >/dev/null 2>&1
gate "C# lint/biçim" dotnet format Briefyn.slnx --verify-no-changes
gate ".NET build/test" dotnet test MeetingWeb.Tests --nologo
NET=$(grep -E "Toplam:|Total:" "$LOG" | tail -1)
NET_KOD=$(grep -cE "Başarısız!|Failed!|error [A-Z]+[0-9]+" "$LOG")
if [ -z "$NET" ] || [ "$NET_KOD" -ne 0 ]; then
  fail ".NET build/test"
fi
GECEN=$(printf '%s' "$NET" | sed -nE 's/.*(Başarılı|Passed): *([0-9]+).*/\2/p')
TOPLAM=$(printf '%s' "$NET" | sed -nE 's/.*(Toplam|Total): *([0-9]+).*/\2/p')
ATLANAN=$(printf '%s' "$NET" | sed -nE 's/.*(Atlanan|Atlandı|Skipped): *([0-9]+).*/\2/p')
ATLANAN_NOTU=""
if [ "${ATLANAN:-0}" -gt 0 ]; then ATLANAN_NOTU=", $ATLANAN atlandı"; fi
echo "Doğrulama: GEÇTİ (CSS, biçim · .NET $GECEN/$TOPLAM$ATLANAN_NOTU)"
