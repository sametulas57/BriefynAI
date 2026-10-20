using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using MeetingWeb.Models;
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

        public ProfileModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
        }

        // Avatars are the only public uploads (see the /uploads guard in Program.cs); one current file per user.
        private string AvatarFolder => Path.Combine(_env.WebRootPath, "uploads", "avatars");

        public string Username { get; set; } = "";
        public string Initials { get; set; } = "";
        public string ProfilePictureUrl { get; set; } = "";

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

        // --- HELPER METOD ---
        private Task LoadProfileDataAsync(ApplicationUser user)
        {
            Username = user.Email ?? "";
            ProfilePictureUrl = UserDisplay.AvatarUrl(user.AvatarFileName);
            Initials = UserDisplay.Initials(user.FullName);
            return Task.CompletedTask;
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