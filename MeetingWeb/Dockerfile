# Geliştirme (Canlı İzleme) için gereken tam donanımlı SDK motoru
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Yapay zekâ hattı sesi ffmpeg ile hazırlar.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app

EXPOSE 8080

# Sistemi doğrudan Canlı İzleme (Hot Reload) modunda başlat
ENTRYPOINT ["dotnet", "watch", "run", "--urls", "http://+:8080"]
