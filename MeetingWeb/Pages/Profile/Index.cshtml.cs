using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.ComponentModel.DataAnnotations;
using MeetingWeb.Models;
using MeetingWeb.Data;
using MeetingWeb.Services;

namespace MeetingWeb.Pages.Profile
{
    // SECURITY GATEWAY: Restrict access to authenticated users only.
    [Authorize]
    public class ProfileModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _db;
        private readonly IAccountDeletionService _deletion;
        private readonly IZoomService _zoom;

        public ProfileModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IWebHostEnvironment env, ApplicationDbContext db, IAccountDeletionService deletion, IZoomService zoom)
        {
            _zoom = zoom;
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
            _db = db;
            _deletion = deletion;
        }

        // Avatars are the only public uploads (see the /uploads guard in Program.cs); one current file per user.
        private string AvatarFolder => Path.Combine(_env.WebRootPath, "uploads", "avatars");

        public string Username { get; set; } = "";
        public string Initials { get; set; } = "";
        public string ProfilePictureUrl { get; set; } = "";
        public bool ZoomEnabled { get; set; }
        public bool ZoomAutoImportEnabled { get; set; }
        public int? ZoomAutoImportWorkspaceId { get; set; }
        public List<MeetingWeb.Models.Workspace> ZoomWorkspaces { get; set; } = new();
        // Email of the linked Zoom account; null when not connected.
        public string? ZoomEmail { get; set; }
        public bool ZoomNeedsReconnect { get; set; }

        [BindProperty]
        public ProfileInputModel ProfileInput { get; set; } = new();

        [BindProperty]
        public PasswordInputModel PasswordInput { get; set; } = new();

        public class ProfileInputModel
        {
            [Required(ErrorMessage = "Ad Soyad boş bırakılamaz.")]
            [StringLength(100, MinimumLength = 3, ErrorMessage = "En az 3 karakter giriniz.")]
            [Display(Name = "Ad Soyad")]
            public string FullName { get; set; } = "";

            [RegularExpression(@"^[5]\d{9}$", ErrorMessage = "Lütfen başına 0 koymadan 10 haneli giriniz (Örn: 5551234567).")]
            [Display(Name = "Telefon Numarası")]
            public string? PhoneNumber { get; set; }
        }

        public class PasswordInputModel
        {
            [Required(ErrorMessage = "Mevcut şifrenizi girmelisiniz.")]
            [DataType(DataType.Password)]
            public string OldPassword { get; set; } = "";

            [Required(ErrorMessage = "Yeni şifre belirlemelisiniz.")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "En az 6 karakter olmalıdır.")]
            [DataType(DataType.Password)]
            public string NewPassword { get; set; } = "";

            [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
            [DataType(DataType.Password)]
            [Compare("NewPassword", ErrorMessage = "Şifreler eşleşmiyor.")]
            public string ConfirmPassword { get; set; } = "";
        }

        // Initialize form data with the current authenticated user's state.
        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Kullanıcı bulunamadı.");

            await LoadProfileDataAsync(user);
            ProfileInput.PhoneNumber = user.PhoneNumber ?? "";
            ProfileInput.FullName = user.FullName;

            return Page();
        }

        // --- PROFILE UPDATE PIPELINE ---
        public async Task<IActionResult> OnPostUpdateProfileAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Kullanıcı bulunamadı.");

            ModelState.Clear();
            if (!TryValidateModel(ProfileInput, nameof(ProfileInput)))
            {
                await LoadProfileDataAsync(user);
                return Page();
            }

            // Uniqueness validation for the phone number field to prevent duplicates.
            if (!string.IsNullOrWhiteSpace(ProfileInput.PhoneNumber))
            {
                var existingPhone = await _userManager.Users
                    .AnyAsync(u => u.PhoneNumber == ProfileInput.PhoneNumber && u.Id != user.Id);

                if (existingPhone)
                {
                    TempData["ErrorMessage"] = "Bu telefon numarası başka bir hesap tarafından kullanılıyor.";
                    await LoadProfileDataAsync(user);
                    return Page();
                }
            }

            user.PhoneNumber = string.IsNullOrWhiteSpace(ProfileInput.PhoneNumber) ? null : ProfileInput.PhoneNumber;
            user.FullName = ProfileInput.FullName;
            if (!await TryUpdateAsync(user)) return RedirectToPage();

            // Refresh the authentication cookie so the new name shows in the layout.
            await _signInManager.RefreshSignInAsync(user);
            TempData["StatusMessage"] = "Profil bilgileriniz başarıyla güncellendi.";
            return RedirectToPage();
        }

        // --- PASSWORD MANAGEMENT PIPELINE ---
        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Kullanıcı bulunamadı.");

            ModelState.Clear();
            if (!TryValidateModel(PasswordInput, nameof(PasswordInput)))
            {
                await LoadProfileDataAsync(user);
                return Page();
            }

            var changePasswordResult = await _userManager.ChangePasswordAsync(user, PasswordInput.OldPassword, PasswordInput.NewPassword);

            if (!changePasswordResult.Succeeded)
            {
                foreach (var error in changePasswordResult.Errors)
                {
                    TempData["ErrorMessage"] = "Hata: " + error.Description;
                }
                return RedirectToPage();
            }

            await _signInManager.RefreshSignInAsync(user);
            TempData["StatusMessage"] = "Şifreniz başarıyla değiştirildi.";
            return RedirectToPage();
        }

        [BindProperty]
        public string DeletionPassword { get; set; } = "";

        public async Task<IActionResult> OnPostRequestDeletionAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();
            if (!await _userManager.CheckPasswordAsync(user, DeletionPassword ?? ""))
            {
                TempData["ErrorMessage"] = "Şifreniz yanlış. Hesap silme isteği alınmadı.";
                return RedirectToPage();
            }

            // Updating the user row first serializes this check with ownership transfers targeting the same user.
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            user.DeletionRequestedAtUtc = DateTime.UtcNow;
            var stampResult = await _userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                TempData["ErrorMessage"] = "Hesap silme isteği kaydedilemedi. Lütfen tekrar deneyin.";
                return RedirectToPage();
            }
            var teams = await _deletion.BlockingTeamsAsync(user.Id);
            if (teams.Count != 0)
            {
                TempData["ErrorMessage"] = "Önce şu ekiplerin kuruculuğunu devredin veya ekipleri silin: " + string.Join(", ", teams);
                return RedirectToPage();
            }
            await tx.CommitAsync();
            await _signInManager.SignOutAsync();
            TempData["StatusMessage"] = "Hesabınız 7 gün sonra silinecek. Bu sürede giriş yaparsanız silme isteğiniz iptal edilir.";
            return RedirectToPage("/Auth/Login");
        }

        // --- AJAX AVATAR UPLOAD HANDLER ---
        public async Task<IActionResult> OnPostUploadAvatarAsync([FromForm] string base64Image)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || string.IsNullOrEmpty(base64Image)) return new JsonResult(new { success = false });

            try
            {
                // Sanitize the Base64 payload by extracting the raw byte stream.
                var base64Data = base64Image.Split(',')[1];
                var bytes = Convert.FromBase64String(base64Data);

                // S10: the file is served publicly as .jpg, so only JPEG content (FF D8 FF) is accepted.
                if (bytes.Length < 3 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
                    return new JsonResult(new { success = false, message = "Lütfen bir fotoğraf seçin." });

                // Ensure the physical directory structure exists.
                var uploadsFolder = AvatarFolder;
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                // A new name per upload busts the browser cache; the previous file is removed so each user keeps one.
                var fileName = $"{user.Id}-{DateTime.UtcNow.Ticks:x}.jpg";
                await System.IO.File.WriteAllBytesAsync(Path.Combine(uploadsFolder, fileName), bytes);

                var previous = user.AvatarFileName;
                user.AvatarFileName = fileName;
                if (!await TryUpdateAsync(user))
                {
                    // The profile still points at the previous photo, so that one stays and the new file goes.
                    DeleteAvatarFile(fileName);
                    return new JsonResult(new { success = false, message = "Fotoğraf kaydedilemedi. Lütfen tekrar deneyin." });
                }
                DeleteAvatarFile(previous);

                await _signInManager.RefreshSignInAsync(user);
                return new JsonResult(new { success = true, avatarUrl = UserDisplay.AvatarUrl(fileName) });
            }
            catch (Exception)
            {
                return new JsonResult(new { success = false, message = "Resim işlenirken sunucu kaynaklı bir hata oluştu." });
            }
        }

        // --- PHYSICAL AVATAR DELETION ---
        public async Task<IActionResult> OnPostRemoveAvatarAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage();

            if (user.AvatarFileName != null)
            {
                var previous = user.AvatarFileName;
                user.AvatarFileName = null;
                if (!await TryUpdateAsync(user)) return RedirectToPage();
                DeleteAvatarFile(previous);
                await _signInManager.RefreshSignInAsync(user);
            }

            TempData["StatusMessage"] = "Profil fotoğrafınız başarıyla kaldırıldı.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostZoomAutoImportAsync(int? workspaceId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Kullanıcı bulunamadı.");

            if (await _zoom.SetAutoImportAsync(user.Id, workspaceId))
                TempData["StatusMessage"] = "Otomatik alma ayarı kaydedildi.";
            else
                TempData["ErrorMessage"] = "Bu ekip seçilemedi.";

            return RedirectToPage("/Profile/Index");
        }

        // --- HELPER METOD ---
        private async Task LoadProfileDataAsync(ApplicationUser user)
        {
            Username = user.Email ?? "";
            ProfilePictureUrl = UserDisplay.AvatarUrl(user.AvatarFileName);
            Initials = UserDisplay.Initials(user.FullName);
            ZoomEnabled = _zoom.IsEnabled;
            var connection = await _zoom.GetConnectionAsync(user.Id);
            ZoomEmail = connection?.ZoomEmail;
            ZoomNeedsReconnect = connection != null && ZoomService.NeedsReconnect(connection.GrantedScopes);
            ZoomAutoImportEnabled = _zoom.IsAutoImportEnabled;
            ZoomAutoImportWorkspaceId = connection?.AutoImportWorkspaceId;
            ZoomWorkspaces = await _db.WorkspaceMembers.AsNoTracking()
                .Where(m => m.UserId == user.Id)
                .Select(m => m.Workspace!)
                .OrderBy(w => w.Name)
                .ToListAsync();
        }

        // Saves the profile; on failure (the phone number was just taken by another account, or the account changed
        // meanwhile) nothing is saved and the user is told so.
        private async Task<bool> TryUpdateAsync(ApplicationUser user)
        {
            try
            {
                if ((await _userManager.UpdateAsync(user)).Succeeded) return true;
                TempData["ErrorMessage"] = "Profiliniz bu arada değişti, kaydedilemedi. Sayfayı yenileyip tekrar deneyin.";
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "Bu telefon numarası başka bir hesap tarafından kullanılıyor.";
            }
            return false;
        }

        // File names come from our own column, but Path.GetFileName keeps the delete inside the avatar folder.
        private void DeleteAvatarFile(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            var path = Path.Combine(AvatarFolder, Path.GetFileName(fileName));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }
}
