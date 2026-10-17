using Hangfire;
using Hangfire.SqlServer;
using Hangfire.Dashboard;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
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

    MeetingWeb.Helpers.EncryptionHelper.Initialize(builder.Configuration);

    // Serilog'u sisteme dahil et (Şu an geçici bootstrap logger'ı kullanıyor)
    builder.Host.UseSerilog();

    // --- 1. Database Connections ---
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString));
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    // --- 2. Identity & Security ---
    builder.Services.AddDefaultIdentity<IdentityUser>(options =>
        options.SignIn.RequireConfirmedAccount = false)
        .AddEntityFrameworkStores<ApplicationDbContext>();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
    });

    // --- 3. Application Settings ---
    builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

    // --- 4. Python API Communication ---
    builder.Services.AddHttpClient("PythonApi", (sp, client) =>
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

    // --- 5. Background Jobs (Hangfire) ---
    // Testlerde Hangfire kurulmaz; testler IBackgroundJobClient yerine sahte bir istemci koyar.
    if (!isTesting)
    {
        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString));

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
    builder.Services.AddScoped<IReencryptionService, ReencryptionService>();
    builder.Services.AddScoped<IMeetingProcessor, MeetingProcessor>();
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
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
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

    builder.Services.AddSignalR();
    builder.Services.AddRazorPages();

    var app = builder.Build();

    // =========================================================================
    // 2. AŞAMA: Veritabanını Otomatik Oluşturma (Migration) ve Tam Loglamaya Geçiş
    // =========================================================================
    // Testler veritabanını kendisi kurar (SQLite, EnsureCreated) ve SQL Server log hedefini kullanmaz.
    if (!isTesting)
    {
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var context = services.GetRequiredService<ApplicationDbContext>();
                Log.Information("Veritabanı kontrol ediliyor ve gerekiyorsa oluşturuluyor (Migration)...");
            
                // Eğer veritabanı hiç yoksa oluşturur, varsa son migration'ları uygular.
                context.Database.Migrate(); 
                Log.Information("Veritabanı hazır!");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Veritabanı oluşturulurken kritik bir hata meydana geldi!");
                throw;
            }
        }

        // Veritabanı KESİN olarak var olduğuna göre Asıl Logger'a geçebiliriz.
        Log.Information("MSSQL Loglama sistemine geçiş yapılıyor...");
        Log.CloseAndFlush(); // Geçici logger'ı kapat ve belleği boşalt

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console() // Docker terminali için konsol AÇIK kalmalı
            .WriteTo.File("logs/ai-service-log-.txt", rollingInterval: RollingInterval.Day) // Dosya yedeği (senin talebin)
            .WriteTo.MSSqlServer(
                connectionString: connectionString,
                sinkOptions: new Serilog.Sinks.MSSqlServer.MSSqlServerSinkOptions
                {
                    TableName = "Logs",
                    AutoCreateSqlTable = true // Tablo yoksa oluşturur
                })
            .CreateLogger();
    }
    // =========================================================================

    // S4 maintenance command (run by hand, never on normal startup):
    //   dotnet run -- reencrypt            -> dry run: counts legacy and unreadable values, changes nothing
    //   dotnet run -- reencrypt --apply    -> converts legacy values to the v2 format, verifying each one
    if (args.Contains("reencrypt"))
    {
        using var scope = app.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IReencryptionService>().RunAsync(apply: args.Contains("--apply"));
        Console.WriteLine($"reencrypt {(args.Contains("--apply") ? "--apply" : "(dry run)")}: {report}");
        return;
    }

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
    }

    app.MapHub<MeetingWeb.Hubs.MeetingHub>("/meetingHub");
    app.MapRazorPages();

    Log.Information("Web uygulaması dinlemeye başladı...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Uygulama beklenmedik şekilde çöktü!");
}
finally
{
    Log.CloseAndFlush();
}

// Custom Authorization Filter for Hangfire Dashboard.
// S6: the dashboard shows job arguments (meeting ids, file paths) and can delete or re-run jobs, so being signed in
// is not enough. Only operators listed in Hangfire:DashboardAdminEmails get in; an empty list locks everyone out.
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true) return false;

        var admins = httpContext.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("Hangfire:DashboardAdminEmails").Get<string[]>() ?? Array.Empty<string>();

        // Accounts are created with UserName == Email, so the Name claim is a valid fallback.
        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.Identity.Name;

        return !string.IsNullOrWhiteSpace(email) &&
               admins.Any(a => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), email, StringComparison.OrdinalIgnoreCase));
    }
}

// WebApplicationFactory'nin (entegrasyon testleri) Program sınıfına erişebilmesi için.
public partial class Program { }
