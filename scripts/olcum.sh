#!/usr/bin/env bash
# Kullanım: scripts/olcum.sh model1 [model2 ...] — uydurma altın setle modelleri ölçer.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

if [[ $# -eq 0 ]]; then echo "Kullanım: scripts/olcum.sh model1 [model2 ...]" >&2; exit 2; fi
if [[ ! -f .env ]]; then echo ".env bulunamadı; GROQ_KEY yalnız .env'den okunur." >&2; exit 1; fi
groq_line=$(grep '^GROQ_KEY=' .env | head -n 1 || true)
groq_key=${groq_line#GROQ_KEY=}
groq_key=${groq_key%$'\r'}
if [[ ${#groq_key} -ge 2 && ( ${groq_key:0:1} == '"' || ${groq_key:0:1} == "'" ) && ${groq_key:0:1} == "${groq_key: -1}" ]]; then
  groq_key=${groq_key:1:${#groq_key}-2}
fi
if [[ -z $groq_key || $groq_key == "$groq_line" ]]; then echo ".env'de GROQ_KEY yok; ölçüm başlatılmadı." >&2; exit 1; fi

dotnet build MeetingWeb.Tests
mkdir -p olcum-sonuc
pids=()
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${pids[@]}"; do wait "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT INT TERM

run_model() {
  local model=$1 summary
  local -a env_args=("BRIEFYN_OLCUM=1" "GROQ_API_KEY=$groq_key" "ANSWERER=groq" "AI_ANSWER_MODEL=$model" "BRIEFYN_OLCUM_MODEL=$model" "BRIEFYN_OLCUM_CIKTI=$PWD/olcum-sonuc")
  if [[ ${BRIEFYN_OLCUM_ARALIK_SN+x} ]]; then env_args+=("BRIEFYN_OLCUM_ARALIK_SN=$BRIEFYN_OLCUM_ARALIK_SN"); fi
  if env "${env_args[@]}" dotnet test MeetingWeb.Tests --no-build --filter 'FullyQualifiedName~AskMeasurement.Run_model_measurement_when_configured'; then
    summary="olcum-sonuc/${model//\//_}-ozet.md"
    echo "Model $model tamamlandı${summary:+: $summary}"
  else
    return 1
  fi
}

for model in "$@"; do
  run_model "$model" &
  pids+=("$!")
done
failed=0
for pid in "${pids[@]}"; do wait "$pid" || failed=1; done
if [[ $failed -ne 0 ]]; then exit 1; fi
echo "Özetler:"
find olcum-sonuc -maxdepth 1 -name '*-ozet.md' -print | sort
