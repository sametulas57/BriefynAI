# Geliştirme (Canlı İzleme) için gereken tam donanımlı SDK motoru
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /app

EXPOSE 8080

# Sistemi doğrudan Canlı İzleme (Hot Reload) modunda başlat
ENTRYPOINT ["dotnet", "watch", "run", "--urls", "http://+:8080"]