# ==========================================
# 1. AŞAMA: FABRİKA (Derleme Aşaması - SDK)
# ==========================================
# Uygulamayı derlemek için gerekli tüm araçları içeren ağır imaj
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce SADECE proje dosyasını kopyala (Python'daki requirements.txt mantığı - Cache için)
COPY ["MeetingWeb.csproj", "./"]
RUN dotnet restore "MeetingWeb.csproj"

# Şimdi kodun tamamını içeri kopyala
COPY . .

# Kodu "Release" modunda (en performanslı halde) derle ve /app/publish klasörüne çıkart
RUN dotnet publish "MeetingWeb.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# 2. AŞAMA: MAĞAZA (Çalıştırma Aşaması - Runtime)
# ==========================================
# Sadece uygulamayı çalıştırmaya yarayan, çok hafif işletim sistemi imajı
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Fabrikada (build aşamasında) üretilen hazır dosyaları bu hafif imaja taşı
COPY --from=build /app/publish .

# .NET 8 ve sonrasında varsayılan port 8080'dir. Docker'a bu portu açmasını söylüyoruz.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Konteyner ayağa kalktığında çalışacak ana komut
ENTRYPOINT ["dotnet", "MeetingWeb.dll"]