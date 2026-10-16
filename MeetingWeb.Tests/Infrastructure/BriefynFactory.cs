using Hangfire;
using MeetingWeb.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Infrastructure
{
    // Boots the real app in the "Testing" environment against an in-memory SQLite database.
    // Each factory instance gets its own database, so test classes never see each other's data.
    public class BriefynFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "briefyn-tests-" + Guid.NewGuid().ToString("N"));

        public FakeBackgroundJobClient Jobs { get; } = new();
        public StubPythonApiHandler PythonApi { get; } = new();

        // Per-factory folders, so parallel test classes never share files.
        public string WebRoot => Path.Combine(_tempRoot, "wwwroot");
        public string AudioRoot => Path.Combine(_tempRoot, "audio");
        public SeedData Seed { get; private set; } = null!;

        static BriefynFactory()
        {
            // Program reads these before the host is built, so they must be environment variables.
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "unused-in-tests");
            Environment.SetEnvironmentVariable("EncryptionSettings__Key", "test-key-32-bytes-long-000000000");
            Environment.SetEnvironmentVariable("EncryptionSettings__IV", "test-iv-16-bytes");
        }

        public BriefynFactory()
        {
            _connection.Open();
            Directory.CreateDirectory(Path.Combine(WebRoot, "uploads", "avatars"));
            Directory.CreateDirectory(AudioRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(WebRoot);
            builder.UseSetting("AudioStorage:RootPath", AudioRoot);

            builder.ConfigureTestServices(services =>
            {
                // Swap SQL Server for SQLite (EF Core 9+ also registers an options-configuration service).
                var efDescriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(IDbContextOptionsConfiguration<ApplicationDbContext>)).ToList();
                foreach (var d in efDescriptors) services.Remove(d);
                services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));

                services.AddSingleton<IBackgroundJobClient>(Jobs);
                services.AddHttpClient("PythonApi").ConfigurePrimaryHttpMessageHandler(() => PythonApi);

                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
            });
        }

        // Builds the server on first use, then creates and seeds the schema once.
        public async Task InitializeAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            Seed = await SeedData.CreateAsync(db);
        }

        public HttpClient ClientFor(string userId)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
            return client;
        }

        public T WithDb<T>(Func<ApplicationDbContext, T> query)
        {
            using var scope = Services.CreateScope();
            return query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
                try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { }
            }
        }
    }
}
