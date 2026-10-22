using System.Text;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;

namespace MeetingWeb.Tests.Security
{
    public class AvatarUploadFixture : IAsyncLifetime
    {
        public BriefynFactory Factory { get; } = new();
        public Task InitializeAsync() => Factory.InitializeAsync();
        public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    }

    // S10: avatars are served publicly from /uploads/avatars as .jpg, so only real JPEG bytes may be written there.
    public class AvatarUploadTests : IClassFixture<AvatarUploadFixture>
    {
        private readonly BriefynFactory _app;
        private SeedData S => _app.Seed;

        public AvatarUploadTests(AvatarUploadFixture fixture) => _app = fixture.Factory;

        private string AvatarFolder => Path.Combine(_app.WebRoot, "uploads", "avatars");

        private async Task<System.Text.Json.JsonElement> UploadAsync(string userId, byte[] bytes)
        {
            var client = _app.ClientFor(userId);
            var token = await GetAntiforgeryTokenAsync(client, "/Profile");
            var dataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
            return await PostFormForJsonAsync(client, "/Profile?handler=UploadAvatar", token, ("base64Image", dataUrl));
        }

        private string? AvatarOf(string userId) =>
            _app.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == userId).AvatarFileName);

        [Fact]
        public async Task Upload_that_is_not_a_jpeg_is_rejected_and_nothing_is_written()
        {
            var before = Directory.GetFiles(AvatarFolder).Length;

            var result = await UploadAsync(S.CarolId, Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));

            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Equal(before, Directory.GetFiles(AvatarFolder).Length);
            Assert.Null(AvatarOf(S.CarolId));
        }

        [Fact]
        public async Task Jpeg_upload_is_saved()
        {
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9 };

            var result = await UploadAsync(S.BobId, jpeg);

            Assert.True(result.GetProperty("success").GetBoolean());
            var fileName = AvatarOf(S.BobId);
            Assert.NotNull(fileName);
            Assert.Equal(jpeg, File.ReadAllBytes(Path.Combine(AvatarFolder, fileName!)));
        }
    }
}
