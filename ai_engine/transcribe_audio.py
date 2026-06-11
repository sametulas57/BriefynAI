import os
from faster_whisper import WhisperModel

# Prompts the model to retain technical jargon instead of translating or hallucinating.
TECH_PROMPT = (
    "Bu bir yazılım toplantısı transkripti. İngilizce teknik terimleri orijinal yazımıyla koru: "
    "backend, frontend, API, endpoint, database, SQL, server, client, microservice, "
    "Docker, Kubernetes, GitHub, pull request, merge, deployment, CI/CD, bug, ticket."
)

def transcribe_audio(
    audio_path: str,
    model_size: str = "medium",
    device: str = "cpu",
    compute_type: str = "float32",
) -> str:
    """
    Transcribes the given audio file into text using the CTranslate2-optimized Faster-Whisper.
    Utilizes VAD (Voice Activity Detection) to skip silent parts and optimizes CPU threading.
    """
    if not os.path.exists(audio_path):
        raise FileNotFoundError(f"Audio file could not be located at path: {audio_path}")

    # Initialize the inference engine with controlled resource allocation
    model = WhisperModel(
        model_size_or_path=model_size, 
        device=device, 
        compute_type=compute_type, 
        cpu_threads=4, 
        num_workers=1
    )

    # Execute transcription with silence filtering and domain-specific context
    segments, info = model.transcribe(
        audio_path,
        vad_filter=True,
        initial_prompt=TECH_PROMPT,
    )

    # Aggregate and sanitize the transcribed text segments efficiently
    text_parts = [seg.text.strip() for seg in segments if seg.text and seg.text.strip()]

    return " ".join(text_parts)