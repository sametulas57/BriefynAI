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
ports=()
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${pids[@]}"; do wait "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT INT TERM

run_model() {
  local model=$1 port=$2 api_key log summary
  api_key=$(openssl rand -hex 16)
  log=$(mktemp)
  ANSWERER=groq AI_ANSWER_MODEL="$model" GROQ_API_KEY="$groq_key" AI_ENGINE_API_KEY="$api_key" \
    ai_engine/.venv/bin/python -m uvicorn api_server:app --app-dir ai_engine --host 127.0.0.1 --port "$port" >"$log" 2>&1 &
  local server=$!
  stop_server() { kill "$server" 2>/dev/null || true; wait "$server" 2>/dev/null || true; }
  trap 'stop_server' EXIT
  trap 'stop_server; exit 130' INT TERM
  pids+=("$server")
  for _ in $(seq 1 60); do
    if curl -fsS "http://127.0.0.1:$port/openapi.json" >/dev/null 2>&1; then break; fi
    if ! kill -0 "$server" 2>/dev/null; then cat "$log" >&2; rm -f "$log"; stop_server; return 1; fi
    sleep 0.5
  done
  if ! curl -fsS "http://127.0.0.1:$port/openapi.json" >/dev/null 2>&1; then
    echo "ai_engine $port 30 saniyede hazır olmadı (model: $model)." >&2
    cat "$log" >&2; rm -f "$log"; stop_server; return 1
  fi
  local -a env_args=("BRIEFYN_OLCUM_URL=http://127.0.0.1:$port/" "BRIEFYN_OLCUM_API_KEY=$api_key" "BRIEFYN_OLCUM_MODEL=$model" "BRIEFYN_OLCUM_CIKTI=$PWD/olcum-sonuc")
  if [[ ${BRIEFYN_OLCUM_ARALIK_SN+x} ]]; then env_args+=("BRIEFYN_OLCUM_ARALIK_SN=$BRIEFYN_OLCUM_ARALIK_SN"); fi
  if env "${env_args[@]}" dotnet test MeetingWeb.Tests --no-build --filter 'FullyQualifiedName~AskMeasurement'; then
    summary="olcum-sonuc/${model//\//_}-ozet.md"
    echo "Model $model tamamlandı${summary:+: $summary}"
  else
    rm -f "$log"
    stop_server
    return 1
  fi
  stop_server
  rm -f "$log"
}

index=0
for model in "$@"; do
  port=$((8100 + index))
  run_model "$model" "$port" &
  pids+=("$!")
  ports+=("$port")
  index=$((index + 1))
done
failed=0
for pid in "${pids[@]}"; do wait "$pid" || failed=1; done
if [[ $failed -ne 0 ]]; then exit 1; fi
echo "Özetler:"
find olcum-sonuc -maxdepth 1 -name '*-ozet.md' -print | sort
