using Hangfire;
using Hangfire.SqlServer;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using System.Threading.RateLimiting;

// 1. AŞAMA: Uygulama ayağa kalkarken kullanılacak geçici ve güvenli (Bootstrap) Logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console() // Konsola yaz (Docker terminali buradan okuyacak)
    .WriteTo.File("logs/bootstrap-log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Uygulama başlatılıyor, servisler yükleniyor...");
    var builder = WebApplication.CreateBuilder(args);

    // Entegrasyon testleri "Testing" ortamında çalışır: SQL Server'a bağlı parçalar (Hangfire, migration, SQL log) atlanır.
    var isTesting = builder.Environment.IsEnvironment("Testing");

    // Appsettings.json'dan connection string'i al
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

    EncryptionHelper.Initialize(builder.Configuration);

    // Serilog'u sisteme dahil et (Şu an geçici bootstrap logger'ı kullanıyor)
    builder.Host.UseSerilog();

    // --- 1. Database Connections ---
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString));
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    // --- 2. Identity & Security ---
    builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
        options.SignIn.RequireConfirmedAccount = false)
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();
    // A deletion request changes the security stamp; reject existing cookies on their next request.
    builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        options.ValidationInterval = TimeSpan.Zero);

    // S9b: Data Protection keys sign the login cookie, antiforgery tokens and TempData. Inside the container they
    // vanished on every rebuild (everyone logged out, open forms failed). They now live in a persistent folder:
    // App_Data/keys under the content root, which Docker keeps on the host through the ./MeetingWeb bind mount.
    var keysPath = builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } configuredKeys
        ? configuredKeys
        : Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("Briefyn");

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Durum/403";
    });

    // --- 3. Application Settings ---
    builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
    builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection("Reminders"));
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

    // --- 4. Python API Communication ---
    builder.Services.AddHttpClient(MeetingProcessor.PythonApiClient, (sp, client) =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["PythonApiSettings:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            client.BaseAddress = new Uri(baseUrl);
        }
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        // S5: the AI engine only accepts requests carrying the shared secret (AI_ENGINE_API_KEY in .env).
        var apiKey = configuration["PythonApiSettings:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
    });

    // ADR-0011: Zoom's API. Redirects are followed by ZoomApiClient itself, so the token goes only to zoom.us hosts.
    builder.Services.Configure<ZoomOptions>(builder.Configuration.GetSection("Zoom"));
    builder.Services.AddHttpClient(ZoomApiClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

    // --- 5. Background Jobs (Hangfire) ---
    // Testlerde Hangfire kurulmaz; testler IBackgroundJobClient yerine sahte bir istemci koyar.
    if (!isTesting)
    {
        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // ADR-0009: Hangfire tables are installed by the "migrate" step; the app user cannot change the schema.
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions { PrepareSchemaIfNecessary = false }));

        builder.Services.AddHangfireServer(options =>
        {
            options.WorkerCount = 1;
        });
    }

    // --- 6. Custom Services ---
    builder.Services.AddScoped<IMeetingAccessService, MeetingAccessService>();
    builder.Services.AddScoped<IActiveWorkspaceService, ActiveWorkspaceService>();
    builder.Services.AddSingleton<IAudioStorage, AudioStorage>();
    builder.Services.AddScoped<IAudioCleanupService, AudioCleanupService>();
    builder.Services.AddScoped<ILogCleanupService, LogCleanupService>();
    builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
    builder.Services.AddScoped<IOverdueReminderService, OverdueReminderService>();
    builder.Services.AddScoped<IMeetingProcessor, MeetingProcessor>();
    builder.Services.AddScoped<ZoomApiClient>();
    builder.Services.AddScoped<IZoomService, ZoomService>();
    builder.Services.AddScoped<ZoomWebhookHandler>();
    builder.Services.AddTransient<IEmailService, EmailService>();
    builder.Services.AddHttpContextAccessor();

    // --- 7. Session & SignalR ---
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromHours(2);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });
    // S8: sharing a report sends real email from our account, so each user gets a small hourly budget.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // Instead of a bare 429 page, send the user back to the meeting with an explanation.
        options.OnRejected = (rejected, _) =>
        {
            var http = rejected.HttpContext;
            // Zoom's notifications get the bare 429.
            if (http.Request.Path.StartsWithSegments(ZoomWebhookHandler.Path))
            {
                ZoomWebhookHandler.SkipStatusPage(http);
                return ValueTask.CompletedTask;
            }
            var tempData = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>().GetTempData(http);
            tempData["ErrorMessage"] = "Bu saat içinde çok fazla rapor gönderdiniz. Lütfen biraz sonra tekrar deneyin.";
            tempData.Save();

            var referer = http.Request.Headers.Referer.ToString();
            var back = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == http.Request.Host.Host ? uri.PathAndQuery : "/History";
            http.Response.Redirect(back);
            return ValueTask.CompletedTask;
        };
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // ADR-0012: the public Zoom endpoint, per caller address.
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments(ZoomWebhookHandler.Path))
                return RateLimitPartition.GetFixedWindowLimiter("zoom-webhook:" + context.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Zoom:WebhookLimitPerMinute", 60),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });

            var isShare = HttpMethods.IsPost(context.Request.Method) &&
                          context.Request.Path.Equals("/Details", StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(context.Request.Query["handler"], "Share", StringComparison.OrdinalIgnoreCase);
            if (!isShare) return RateLimitPartition.GetNoLimiter("unlimited");

            var userKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter("share:" + userKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("EmailSettings:ShareLimitPerHour", 10),
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            });
        });
    });

    // Uploads: allow recordings up to Upload:MaxMegabytes (+ room for the form itself); the page explains the limit.
    builder.Services.AddOptions<Microsoft.AspNetCore.Http.Features.FormOptions>().Configure<IConfiguration>((o, c) =>
        o.MultipartBodyLengthLimit = (c.GetValue("Upload:MaxMegabytes", 300) + 5) * 1024L * 1024L);
    builder.Services.AddOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>().Configure<IConfiguration>((o, c) =>
        o.Limits.MaxRequestBodySize = (c.GetValue("Upload:MaxMegabytes", 300) + 5) * 1024L * 1024L);

    builder.Services.AddSignalR();
    builder.Services.AddRazorPages();

    var app = builder.Build();

    // =========================================================================
    // 2. AŞAMA: Şema kurulumu ayrı adımda (ADR-0009), sonra tam loglamaya geçiş
    // =========================================================================
    //   dotnet run -- migrate   -> EF migration'ları ve Hangfire tablolarını kurar, çıkar (compose "migrate" servisi,
    //                              şema yetkili briefyn_migrator ile). Web (briefyn_app) yalnız okur/yazar.
    if (args.Contains("migrate"))
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
        // Creating the storage with PrepareSchemaIfNecessary installs (or upgrades) Hangfire's tables.
        _ = new SqlServerStorage(connectionString, new SqlServerStorageOptions { PrepareSchemaIfNecessary = true });
        Log.Information("migrate: veritabanı şeması hazır.");
        return;
    }

    // Testler veritabanını kendisi kurar (SQLite, EnsureCreated) ve SQL Server log hedefini kullanmaz.
    if (!isTesting)
    {
        using (var scope = app.Services.CreateScope())
        {
            var pending = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetPendingMigrations().ToList();
            if (pending.Count > 0)
            {
                throw new InvalidOperationException($"Veritabanı şeması eski ({string.Join(", ", pending)}). Önce migrate adımını çalıştırın.");
            }
        }

        // Veritabanı KESİN olarak var olduğuna göre Asıl Logger'a geçebiliriz.
        Log.Information("MSSQL Loglama sistemine geçiş yapılıyor...");
        Log.CloseAndFlush(); // Geçici logger'ı kapat ve belleği boşalt

        // ADR-0009: framework ayrıntısı (istek adresleri ve davet bağlantıları, SQL komutları) yalnız uyarıdan itibaren;
        // veritabanına yalnız uyarı ve hata yazılır (30 gün tutulur, LogCleanupService). Logs tablosu migration'da kurulur.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console() // Docker terminali için konsol AÇIK kalmalı
            .WriteTo.File("logs/ai-service-log-.txt", rollingInterval: RollingInterval.Day) // Dosya yedeği (senin talebin)
            .WriteTo.MSSqlServer(
                connectionString: connectionString,
                sinkOptions: new Serilog.Sinks.MSSqlServer.MSSqlServerSinkOptions
                {
                    TableName = "Logs",
                    AutoCreateSqlTable = false
                },
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
            .CreateLogger();
    }
    // =========================================================================

    // --- HTTP REQUEST PIPELINE (MIDDLEWARE) ---
    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    // DOCKER İÇİN HTTPS YÖNLENDİRMESİNİ ATLA (Edge Case Koruması)
    if (!app.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
    {
        app.UseHttpsRedirection();
    }

    // S2: wwwroot/uploads may still hold recordings from before the private audio store; only avatars stay public.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/uploads") && !path.StartsWithSegments("/uploads/avatars"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });

    // The framework's built-in account pages (/Identity/...) come with AddDefaultIdentity; the app has its own sign-up,
    // sign-in and profile pages and uses only the built-in logout. The rest is closed: its sign-up skips the consent and
    // the personal space, and its account deletion does not fit the v2 schema (ADR-0009).
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/Identity") && !path.StartsWithSegments("/Identity/Account/Logout"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });

    // Empty error responses (404, 401, ...) are re-rendered as a friendly page instead of the browser's blank one.
    app.UseStatusCodePagesWithReExecute("/Durum/{0}");

    app.UseStaticFiles();
    app.UseRouting();
    app.UseSession();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    if (!isTesting)
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireAuthorizationFilter() }
        });

        // Daily sweep of recordings left behind by failed analyses (see AudioCleanupService).
        app.Services.GetRequiredService<IRecurringJobManager>()
            .AddOrUpdate<IAudioCleanupService>("audio-cleanup", s => s.RunAsync(), Cron.Daily());
        app.Services.GetRequiredService<IRecurringJobManager>()
            .AddOrUpdate<ILogCleanupService>("log-cleanup", s => s.RunAsync(), Cron.Daily());
        app.Services.GetRequiredService<IRecurringJobManager>()
            .AddOrUpdate<IAccountDeletionService>("account-deletion", s => s.RunAsync(), Cron.Daily());
        var reminderHour = builder.Configuration.GetValue<int>("Reminders:Hour", 9);
        app.Services.GetRequiredService<IRecurringJobManager>()
            .AddOrUpdate<IOverdueReminderService>("overdue-reminders", s => s.RunAsync(), Cron.Daily(reminderHour), new RecurringJobOptions { TimeZone = DisplayTime.Zone });
    }

    app.MapHub<MeetingWeb.Hubs.MeetingHub>("/meetingHub");
    app.MapPost(ZoomWebhookHandler.Path, (HttpRequest request, ZoomWebhookHandler handler) => handler.HandleAsync(request));
    app.MapRazorPages();

    Log.Information("Web uygulaması dinlemeye başladı...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Uygulama beklenmedik şekilde çöktü!");
    // A failed "migrate" must not look successful to Docker Compose, which would then start the web service.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// WebApplicationFactory'nin (entegrasyon testleri) Program sınıfına erişebilmesi için.
public partial class Program { }
