# 1. Taban İmaj (Hafif ve güncel bir Python sürümü seçiyoruz)
FROM python:3.12.13-slim

# 2. Konteyner içindeki çalışma klasörümüzü belirliyoruz
WORKDIR /app

# 3. İşletim Sistemi Bağımlılıkları (Whisper'ın çalışması için FFmpeg KURTARICIDIR)
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# 4. Önce SADECE alışveriş listesini (requirements) kopyala
# Bu bir Senior taktiğidir. Kodların değişse bile Docker baştan kütüphane indirmeyip önbellek (cache) kullanır.
COPY requirements.txt .

# 5. Python kütüphanelerini kur
RUN pip install --no-cache-dir -r requirements.txt

# 6. Şimdi diğer tüm kodları (api_server.py, transcribe_audio.py vb.) içeri kopyala (ikinci nokta WORKDIR oluyor yani)
COPY . .

# 7. Konteynerin dış dünyaya açacağı port (C# buradan istek atacak)
EXPOSE 8000

# 8. Konteyner ayağa kalktığında çalıştırılacak nihai komut
# Not: api_server.py içindeki FastAPI değişkeninin adının "app" olduğu varsayılmıştır.
CMD ["uvicorn", "api_server:app", "--host", "0.0.0.0", "--port", "8000"]