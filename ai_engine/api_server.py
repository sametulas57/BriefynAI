import os
import hmac
import logging
import tempfile
import uvicorn
from pathlib import Path
from fastapi import FastAPI, UploadFile, File, HTTPException, Request
from fastapi.responses import JSONResponse

# AI providers (Groq today) live behind small interfaces in providers/; see providers/__init__.py.
from providers import build_summarizer, build_transcriber
from providers.speakers import is_only_hallucinated_captions, parse_vtt
from pydantic import BaseModel


def transcribe_audio(path: str, speakers_vtt: str | None = None) -> str:
    return build_transcriber().transcribe(path, speakers_vtt)


def generate_meeting_summary(text: str) -> dict:
    return build_summarizer().summarize(text)

# Configure logging for production-level observability
logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')

app = FastAPI(
    title="Briefyn AI - Intelligence Core API",
    description="Thin service in front of the AI providers: speech-to-text (Groq Whisper) and summarization (Groq LLM).",
    version="2.0"
)

# --- SECURITY (S5) ---
# Only the .NET backend may call this service: it sends the shared secret in X-Api-Key.
# Without a configured secret the service refuses everything (fail closed) instead of running open.
ALLOWED_AUDIO_EXTENSIONS = {".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm", ".mp4"}
COPY_CHUNK_BYTES = 1024 * 1024


def max_upload_bytes() -> int:
    return int(os.getenv("AI_ENGINE_MAX_UPLOAD_MB", "500")) * 1024 * 1024


class UploadTooLarge(Exception):
    pass


@app.middleware("http")
async def require_api_key(request: Request, call_next):
    # Runs before the multipart body is read, so unauthenticated or oversized uploads never touch the disk.
    if request.url.path.startswith("/api/"):
        expected = os.getenv("AI_ENGINE_API_KEY", "")
        if not expected:
            logging.error("AI_ENGINE_API_KEY is not set; refusing request (fail closed).")
            return JSONResponse(status_code=503, content={"detail": "Service is not configured."})

        provided = request.headers.get("x-api-key", "")
        if not hmac.compare_digest(provided.encode(), expected.encode()):
            return JSONResponse(status_code=401, content={"detail": "Unauthorized."})

        declared = request.headers.get("content-length")
        if declared and declared.isdigit() and int(declared) > max_upload_bytes():
            return JSONResponse(status_code=413, content={"detail": "Upload is too large."})

    return await call_next(request)


def safe_temp_path(client_file_name: str | None) -> Path:
    # The client's file name never becomes part of a path: only a known audio extension survives.
    extension = Path(client_file_name or "").suffix.lower()
    if extension not in ALLOWED_AUDIO_EXTENSIONS:
        extension = ".bin"
    handle, path = tempfile.mkstemp(prefix="briefyn_", suffix=extension)
    os.close(handle)
    return Path(path)


def save_with_limit(upload: UploadFile, destination: Path) -> int:
    # Content-Length can be missing (chunked uploads), so the limit is also enforced while copying.
    limit, written = max_upload_bytes(), 0
    with open(destination, "wb") as buffer:
        while chunk := upload.file.read(COPY_CHUNK_BYTES):
            written += len(chunk)
            if written > limit:
                raise UploadTooLarge()
            buffer.write(chunk)
    return written


@app.post("/api/process-meeting")
async def process_meeting(file: UploadFile = File(...), speakers: UploadFile | None = File(None)):
    """
    Primary orchestration endpoint:
    1) Receives audio binary via multipart/form-data.
    2) Transcribes audio with the configured provider (Groq Whisper, see providers/).
    3) Summarizes the transcript with the configured LLM provider.
    4) Returns structured JSON to the .NET Backend.
    """
    
    # Unique, OS-managed temporary path; the client's file name is not used (path traversal).
    temp_file_path = safe_temp_path(file.filename)

    try:
        # STEP 1: PERSISTENCE - Stream uploaded file to local storage for processing
        size = save_with_limit(file, temp_file_path)
        logging.info(f"📥 Received audio upload ({size} bytes).")
        
        # STEP 2: TRANSCRIPTION - Convert speech to text
        logging.info("🎧 Initializing Speech-to-Text engine...")
        speakers_vtt = None
        if speakers is not None:
            data = await speakers.read(2 * 1024 * 1024 + 1)
            if len(data) > 2 * 1024 * 1024:
                raise UploadTooLarge()
            try:
                speakers_vtt = data.decode("utf-8")
            except UnicodeDecodeError:
                pass
        transcribed_text = transcribe_audio(str(temp_file_path), speakers_vtt) if speakers_vtt else transcribe_audio(str(temp_file_path))
        
        speaker_names = {cue.speaker for cue in parse_vtt(speakers_vtt) if cue.speaker} if speakers_vtt else set()
        if not transcribed_text or is_only_hallucinated_captions(transcribed_text, speaker_names):
            raise HTTPException(status_code=422, detail="no_speech")
            
        logging.info("✅ Transcription completed successfully.")

        # STEP 3: SUMMARIZATION - Extract context and actions via the LLM provider.
        # If only this step fails, the transcript is still returned so the backend can keep it and retry the summary alone.
        logging.info("🧠 Executing LLM summarization pipeline...")
        try:
            summary_result = generate_meeting_summary(transcribed_text)
        except Exception:
            logging.exception("❌ Summarization failed; returning the transcript without a summary.")
            summary_result = None

        if not summary_result:
            return JSONResponse(content={"success": False, "transkript": transcribed_text, "summary": None})

        logging.info("✅ Processing pipeline complete. Dispatching results to .NET backend.")

        # STEP 4: DISPATCH - Return finalized intelligence payload
        return JSONResponse(content={
            "success": True,
            "transkript": transcribed_text,
            "summary": summary_result
        })
        
    except HTTPException as he:
        # Re-raise known application errors
        raise he
    except UploadTooLarge:
        raise HTTPException(status_code=413, detail="Upload is too large.")
    except Exception:
        # Full details stay in the server log; the caller only learns that something failed.
        logging.exception("❌ CRITICAL SERVER ERROR")
        raise HTTPException(status_code=500, detail="Internal Server Error")
        
    finally:
        # STEP 5: RESOURCE CLEANUP - Crucial for maintaining stateless server health
        if temp_file_path.exists():
            os.remove(temp_file_path)
            logging.info("🗑️ Temporary audio artifacts purged.")

class SummarizeRequest(BaseModel):
    transkript: str


@app.post("/api/summarize")
def summarize(request: SummarizeRequest):
    # Retry path: the backend already stores the transcript, so only the summary is regenerated.
    try:
        summary = generate_meeting_summary(request.transkript)
    except Exception:
        logging.exception("❌ Summarization failed")
        raise HTTPException(status_code=500, detail="Internal Server Error")
    if not summary:
        raise HTTPException(status_code=500, detail="Internal Server Error")
    return {"success": True, "summary": summary}


# Entry point for the Uvicorn ASGI server
if __name__ == "__main__":
    # Dışarıdan PORT değişkenini oku, eğer bulamazsan varsayılan olarak 8000 kullan
    port = int(os.getenv("PORT", 8000))
    
    # Docker'da ve mikroservislerde çalışabilmesi için host "0.0.0.0" olmalıdır
    uvicorn.run(app, host="0.0.0.0", port=port)
