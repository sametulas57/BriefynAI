import os
import uuid
import logging
import shutil
import uvicorn
from pathlib import Path
from fastapi import FastAPI, UploadFile, File, HTTPException
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

@app.post("/api/process-meeting")
async def process_meeting(file: UploadFile = File(...)):
    """
    Primary orchestration endpoint:
    1) Receives audio binary via multipart/form-data.
    2) Transcribes audio using optimized Faster-Whisper.
    3) Summarizes transcript using local LLM via Ollama.
    4) Returns structured JSON to the .NET Backend.
    """
    
    # Generate a unique temporary path to prevent filename collisions during concurrent requests
    temp_file_path = Path(f"temp_{uuid.uuid4()}_{file.filename}")
    
    try:
        # STEP 1: PERSISTENCE - Stream uploaded file to local storage for processing
        with open(temp_file_path, "wb") as buffer:
            shutil.copyfileobj(file.file, buffer)
            
        logging.info(f"📥 Received file: {file.filename}")
        
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
    except Exception as e:
        # Catch-all for unhandled exceptions to prevent service crash
        logging.error(f"❌ CRITICAL SERVER ERROR: {str(e)}")
        raise HTTPException(status_code=500, detail=f"Internal Server Error: {str(e)}")
        
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