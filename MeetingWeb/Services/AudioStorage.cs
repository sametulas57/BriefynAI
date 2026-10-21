namespace MeetingWeb.Services
{
    // Private store for meeting recordings. Files live OUTSIDE wwwroot, so the static file middleware can never serve them.
    // Meeting.AudioFileName holds only the stored file name; the absolute path is resolved here.
    public interface IAudioStorage
    {
        string RootPath { get; }
        Task<string> SaveAsync(IFormFile file);
        // Copies at most maxBytes; returns null (and keeps nothing) when the content is larger.
        Task<string?> SaveAsync(Stream content, string fileName, long maxBytes);
        Task<bool> SaveSidecarAsync(string storedName, Stream content, long maxBytes);
        string GetPath(string storedName);
        string? GetSidecarPath(string storedName);
        bool Delete(string? storedName);
    }

    public class AudioStorage : IAudioStorage
    {
        private readonly string _legacyRoot;
        private readonly ILogger<AudioStorage> _logger;

        public string RootPath { get; }

        public AudioStorage(IConfiguration configuration, IWebHostEnvironment environment, ILogger<AudioStorage> logger)
        {
            RootPath = configuration["AudioStorage:RootPath"] is { Length: > 0 } configured
                ? configured
                : Path.Combine(environment.ContentRootPath, "App_Data", "audio");
            // Recordings uploaded before this store existed sit in wwwroot/uploads (now blocked from public access).
            _legacyRoot = Path.Combine(environment.WebRootPath ?? environment.ContentRootPath, "uploads");
            _logger = logger;
            Directory.CreateDirectory(RootPath);
        }

        public async Task<string> SaveAsync(IFormFile file)
        {
            // PATH TRAVERSAL PREVENTION: keep only the client's file name, prefixed with a high-entropy GUID.
            var storedName = Guid.NewGuid() + "_" + Path.GetFileName(file.FileName);
            await using var stream = new FileStream(Path.Combine(RootPath, storedName), FileMode.CreateNew);
            await file.CopyToAsync(stream);
            return storedName;
        }

        public async Task<string?> SaveAsync(Stream content, string fileName, long maxBytes)
        {
            var storedName = Guid.NewGuid() + "_" + Path.GetFileName(fileName);
            var path = Path.Combine(RootPath, storedName);
            var complete = false;
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > maxBytes) return null;
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                }
                complete = true;
                return storedName;
            }
            finally
            {
                if (!complete) File.Delete(path);
            }
        }

        public async Task<bool> SaveSidecarAsync(string storedName, Stream content, long maxBytes)
        {
            var path = GetPath(storedName) + ".vtt";
            var created = false;
            var complete = false;
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew);
                created = true;
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > maxBytes) return false;
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                }
                complete = true;
                return true;
            }
            finally
            {
                if (created && !complete) File.Delete(path);
            }
        }

        // Accepts both new values ("<guid>_name.m4a") and legacy ones ("/uploads/<guid>_name.m4a").
        public string GetPath(string storedName)
        {
            var fileName = Path.GetFileName(storedName);
            var path = Path.Combine(RootPath, fileName);
            if (!File.Exists(path))
            {
                var legacyPath = Path.Combine(_legacyRoot, fileName);
                if (File.Exists(legacyPath)) return legacyPath;
            }
            return path;
        }

        public string? GetSidecarPath(string storedName)
        {
            var path = GetPath(storedName) + ".vtt";
            return File.Exists(path) ? path : null;
        }

        public bool Delete(string? storedName)
        {
            if (string.IsNullOrEmpty(storedName)) return false;

            var path = GetPath(storedName);
            var sidecar = GetSidecarPath(storedName);
            try
            {
                var existed = File.Exists(path);
                if (existed) File.Delete(path);
                if (sidecar != null) File.Delete(sidecar);
                return existed || sidecar != null;
            }
            catch (IOException)
            {
                // The cleanup job retries unreferenced files later, so a locked file is not fatal.
                _logger.LogWarning("Audio file could not be deleted.");
                return false;
            }
        }
    }
}
