import os
import hmac
import logging
import tempfile
import uvicorn
from pathlib import Path
from fastapi import FastAPI, UploadFile, File, HTTPException, Request
from fastapi.responses import JSONResponse

# Internal module imports for AI processing pipeline
from transcribe_audio import transcribe_audio
from summarize_text import generate_meeting_summary

# Configure logging for production-level observability
logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')

app = FastAPI(
    title="Briefyn AI - Intelligence Core API",
    description="Microservice responsible for Speech-to-Text (Whisper) and LLM-based summarization (Qwen2.5).",
    version="1.0"
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
async def process_meeting(file: UploadFile = File(...)):
    """
    Primary orchestration endpoint:
    1) Receives audio binary via multipart/form-data.
    2) Transcribes audio using optimized Faster-Whisper.
    3) Summarizes transcript using local LLM via Ollama.
    4) Returns structured JSON to the .NET Backend.
    """
    
    # Unique, OS-managed temporary path; the client's file name is not used (path traversal).
    temp_file_path = safe_temp_path(file.filename)

    try:
        # STEP 1: PERSISTENCE - Stream uploaded file to local storage for processing
        size = save_with_limit(file, temp_file_path)
        logging.info(f"📥 Received audio upload ({size} bytes).")
        
        # STEP 2: TRANSCRIPTION - Convert speech to text via Whisper
        logging.info("🎧 Initializing Speech-to-Text engine...")
        transcribed_text = transcribe_audio(str(temp_file_path))
        
        if not transcribed_text:
            raise HTTPException(status_code=500, detail="Transcription failed: Audio yielded no recognizable text.")
            
        logging.info("✅ Transcription completed successfully.")

        # STEP 3: SUMMARIZATION - Extract context and actions via local LLM
        logging.info("🧠 Executing LLM summarization pipeline...")
        summary_result = generate_meeting_summary(transcribed_text)
        
        if not summary_result:
             raise HTTPException(status_code=500, detail="LLM processing failed: Summarization engine returned null or invalid schema.")

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

# Entry point for the Uvicorn ASGI server
if __name__ == "__main__":
    # Dışarıdan PORT değişkenini oku, eğer bulamazsan varsayılan olarak 8000 kullan
    port = int(os.getenv("PORT", 8000))
    
    # Docker'da ve mikroservislerde çalışabilmesi için host "0.0.0.0" olmalıdır
    uvicorn.run(app, host="0.0.0.0", port=port)