import json
import logging
import re
import ollama

# Sunucu taraflı loglama ayarı
logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')

def generate_meeting_summary(transkript_metni: str, model_name: str = 'qwen2.5:14b-instruct') -> dict | None:
    """
    Analyzes the given meeting transcript and extracts structured data using a local LLM via Ollama.
    Forces JSON output to ensure API compatibility with the C# backend.
    """
    logging.info(f"🤖 {model_name} (Ollama) özetleme işlemi başlatılıyor...")
    
    # SİSTEM KOMUTU (PROMPT ENGINEERING)
    system_prompt = """Sen Arksoft şirketi için çalışan profesyonel ve analitik bir Toplantı Asistanısın. 
    Görevin, sana verilen toplantı deşifresini (transkript) okumak ve sadece istenen başlıklarda yapılandırılmış bir özet çıkarmaktır.

    KURALLAR:
    1. Sadece Türkçe yanıt ver.
    2. Metinde geçmeyen hiçbir bilgiyi uydurma (halüsinasyon yapma).
    3. Eğer bir başlık için yeterli bilgi yoksa, "Bu konuda net bir bilgi bulunamadı" yaz veya boş liste [] bırak.
    4. YANITIN KESİNLİKLE JSON FORMATINDA OLMALIDIR. Başında veya sonunda açıklama YAZMA.

    JSON FORMATI:
    {
      "toplanti_konusu": "Toplantının veya kaydın ana amacı nedir? (1-2 cümlelik özet)",
      "gorusulen_konular": [
        "Konu 1 ve detayı",
        "Konu 2 ve detayı"
      ],
      "alinan_kararlar": [
        "Karar 1",
        "Karar 2"
      ],
      "aksiyon_maddeleri": [
        "Kim, ne yapacak? (Örn: Ahmet sunucu maliyetlerini çıkaracak)"
      ]
    }
    """

    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": f"İşte analiz etmen gereken toplantı dökümü:\n\n{transkript_metni}"}
    ]

    try:
        # LLM INFERENCE (JSON FORMAT FORCING)
        response = ollama.chat(
            model=model_name, 
            messages=messages,
            format='json' # DİKKAT: Modeli kesinlikle JSON üretmeye zorlar!
        )
        
        ai_cevabi = response['message']['content']
        
        # JSON PARSING & SANITIZATION PIPELINE
        try:
            # Önce doğrudan parse etmeyi dene
            summary_json = json.loads(ai_cevabi)
            logging.info("✅ Özet başarıyla JSON formatında çıkarıldı!")
            return summary_json
            
        except json.JSONDecodeError:
            # FALLBACK: Model JSON kuralını bozup metin arasına gizlediyse Regex ile kurtar
            logging.warning("⚠️ Model doğrudan JSON dönmedi, Regex kurtarma katmanı devreye giriyor...")
            match = re.search(r'\{.*\}', ai_cevabi, re.DOTALL)
            
            if match:
                summary_json = json.loads(match.group(0))
                logging.info("✅ Özet Regex ile başarıyla kurtarıldı!")
                return summary_json
            else:
                raise ValueError("JSON verisi bulunamadı.")

    except Exception as e:
        logging.error(f"❌ Ollama ile iletişimde veya JSON işlemesinde hata oluştu: {e}")
        # Hata durumunda C# tarafının çökmemesi için en azından boş ama formatlı bir yapı dönüyoruz
        return {
            "toplanti_konusu": "Analiz Sırasında Sunucu Hatası Oluştu",
            "gorusulen_konular": [],
            "alinan_kararlar": [],
            "aksiyon_maddeleri": []
        }