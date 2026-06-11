using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MeetingWeb.Pages.Auth
{
    // SECURITY GATEWAY: Restrict access to authenticated users only.
    [Authorize]
    public class ProfileModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IWebHostEnvironment _env;

        public ProfileModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
        }

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

            var claims = await _userManager.GetClaimsAsync(user);
            ProfileInput.FullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? "İsimsiz";

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

            await _userManager.SetPhoneNumberAsync(user, ProfileInput.PhoneNumber);

            // Update custom identity Claims to reflect name changes across the application.
            var claims = await _userManager.GetClaimsAsync(user);
            var oldFullNameClaim = claims.FirstOrDefault(c => c.Type == "FullName");

            if (oldFullNameClaim != null)
            {
                await _userManager.RemoveClaimAsync(user, oldFullNameClaim);
            }
            await _userManager.AddClaimAsync(user, new Claim("FullName", ProfileInput.FullName));

            // Invalidate and refresh the authentication cookie to apply claim modifications.
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
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "avatars");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                // Persist the image. Using User.Id guarantees a single file per user, preventing storage bloat.
                var fileName = $"{user.Id}.jpg";
                var filePath = Path.Combine(uploadsFolder, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

                // Update the user's Avatar Claim.
                var claims = await _userManager.GetClaimsAsync(user);
                var oldAvatarClaim = claims.FirstOrDefault(c => c.Type == "AvatarUrl");
                if (oldAvatarClaim != null) await _userManager.RemoveClaimAsync(user, oldAvatarClaim);

                // Implement Cache Busting via URL parameters (Ticks) to force browser refresh.
                var newAvatarUrl = $"/uploads/avatars/{fileName}?t={DateTime.UtcNow.Ticks}";
                await _userManager.AddClaimAsync(user, new Claim("AvatarUrl", newAvatarUrl));

                await _signInManager.RefreshSignInAsync(user);
                return new JsonResult(new { success = true, avatarUrl = newAvatarUrl });
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

            // Perform hard deletion from the physical file system.
            var filePath = Path.Combine(_env.WebRootPath, "uploads", "avatars", $"{user.Id}.jpg");
            if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);

            // Strip the Avatar Claim from the user's identity profile.
            var claims = await _userManager.GetClaimsAsync(user);
            var avatarClaim = claims.FirstOrDefault(c => c.Type == "AvatarUrl");
            if (avatarClaim != null)
            {
                await _userManager.RemoveClaimAsync(user, avatarClaim);
                await _signInManager.RefreshSignInAsync(user);
            }

            TempData["StatusMessage"] = "Profil fotoğrafınız başarıyla kaldırıldı.";
            return RedirectToPage();
        }

        // --- HELPER METOD ---
        private async Task LoadProfileDataAsync(IdentityUser user)
        {
            Username = user.Email ?? "";

            var claims = await _userManager.GetClaimsAsync(user);
            var fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? "İsimsiz";

            ProfilePictureUrl = claims.FirstOrDefault(c => c.Type == "AvatarUrl")?.Value ?? "";

            var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Initials = nameParts.Length > 1
                ? (nameParts[0][0].ToString() + nameParts[nameParts.Length - 1][0].ToString()).ToUpper()
                : fullName.Substring(0, Math.Min(fullName.Length, 2)).ToUpper();
        }
    }
}