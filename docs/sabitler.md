# Sabit değerler

| Değer | Doğrulandığı yer |
|---|---|
| Web dış portu: `8080` (`8080:8080`) | `docker-compose.yml` (`web.ports`) |
| Yapay zekâ servisi dış portu: `127.0.0.1:8000` | `docker-compose.yml` (`ai_engine.ports`) |
| Veritabanı dış portu: `127.0.0.1:1433` | `docker-compose.yml` (`db.ports`) |
| `db-init`, `migrate` ve `db-readonly` dış port açmaz | `docker-compose.yml` (ilgili servislerde `ports` yok) |
| Önizleme portları değişken; yerel girişte test hesabı kullanılır | `AGENTS.md` ("Ekranda kontrol", "Test hesabı") |
| Docker Compose proje adı: `briefynai` | `docker-compose.yml` (`name`) |
| Açıkça tanımlı container adları: `briefyn-sql`, `briefyn-ai`, `briefyn-web`; diğer servislerin sabit `container_name` değeri yok | `docker-compose.yml` (`container_name`) |
| GitHub deposu: `sametulas57/BriefynAI` | `git remote -v` (`origin`) |
| Proje klasörü: `/Users/sametulaskarasu/Projeler/BriefynAI` | Çalışma klasörü (`pwd -P`) |
| Test hesabının görünen adı: `Test Kullanıcı`; e-posta ve şifre değişkenleri: `TEST_USER_EMAIL`, `TEST_USER_PASSWORD` | `AGENTS.md` ("Test hesabı"); `.env` (yalnız değişken adları) |
| Veritabanı adı: `BriefynDB` | `docker-compose.yml` (`migrate` ve `web` bağlantıları) |
| Hangfire panosu yolu: `/hangfire` | `MeetingWeb/Program.cs` (`UseHangfireDashboard`) |
