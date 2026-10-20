using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;

namespace MeetingWeb.Pages.Auth
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _context;

        // Constructor injection for Identity and EF Core dependencies.
        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        // Data Transfer Object (DTO) for incoming registration form payload.
        public class InputModel
        {
            [Required(ErrorMessage = "Ad Soyad alanı zorunludur.")]
            [StringLength(100, ErrorMessage = "{0} en az {2} ve en fazla {1} karakter uzunluğunda olmalıdır.", MinimumLength = 3)]
            [Display(Name = "Ad Soyad")]
            public string FullName { get; set; } = string.Empty;

            [Required(ErrorMessage = "E-posta alanı zorunludur.")]
            [EmailAddress(ErrorMessage = "Lütfen geçerli bir e-posta adresi giriniz.")]
            public string Email { get; set; } = string.Empty;

            // Strict regex validation: Turkish mobile format without leading zero.
            [RegularExpression(@"^[5]\d{9}$", ErrorMessage = "Lütfen telefon numaranızı başında 0 olmadan 10 haneli giriniz (Örn: 5551234567).")]
            [Display(Name = "Telefon Numarası")]
            public string? PhoneNumber { get; set; }

            [Required(ErrorMessage = "Şifre alanı zorunludur.")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifreniz en az 6 karakter uzunluğunda olmalıdır.")]
            [DataType(DataType.Password)]
            [Display(Name = "Şifre")]
            public string Password { get; set; } = string.Empty;

            // Security enforcement: Password match validation prior to hashing.
            [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
            [DataType(DataType.Password)]
            [Display(Name = "Şifre Tekrar")]
            [Compare("Password", ErrorMessage = "Şifreler birbiriyle eşleşmiyor. Lütfen kontrol edin.")]
            public string ConfirmPassword { get; set; } = string.Empty;

            // Range etiketi tamamen kaldırıldı, kontrol aşağıda manuel yapılacak.
            [Display(Name = "KVKK Onayı")]
            public bool IsKvkkAccepted { get; set; }
        }

        // HTTP POST handler for the registration workflow.
        public async Task<IActionResult> OnPostAsync()
        {
            // DÜZELTME: Manuel KVKK Kontrolü (ModelState.IsValid kontrolünden hemen önce yapıyoruz)
            if (!Input.IsKvkkAccepted)
            {
                ModelState.AddModelError("Input.IsKvkkAccepted", "Kayıt olmak için KVKK metnini onaylamanız gerekmektedir.");
            }

            if (ModelState.IsValid)
            {
                // SECURITY GATEWAY: Enforce global uniqueness constraint on phone numbers.
                if (!string.IsNullOrEmpty(Input.PhoneNumber))
                {
                    var phoneExists = await _userManager.Users.AnyAsync(u => u.PhoneNumber == Input.PhoneNumber);
                    if (phoneExists)
                    {
                        ModelState.AddModelError("Input.PhoneNumber", "Bu telefon numarası zaten başka bir hesap tarafından kullanılıyor.");
                        return Page();
                    }
                }

                // Initialize a new identity principal.
                var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email, PhoneNumber = Input.PhoneNumber, FullName = Input.FullName };
                IdentityResult result;
                try
                {
                    result = await _userManager.CreateAsync(user, Input.Password);
                }
                catch (DbUpdateException)
                {
                    // Another sign-up took the same phone number a moment ago; the database's unique index refused this one.
                    ModelState.AddModelError("Input.PhoneNumber", "Bu telefon numarası zaten başka bir hesap tarafından kullanılıyor.");
                    return Page();
                }

                if (result.Succeeded)
                {
                    // ZERO-FRICTION ONBOARDING: Automatically provision a default isolated Workspace, owned by the new user.
                    var personalWorkspace = new MeetingWeb.Models.Workspace
                    {
                        Name = "Kişisel Alanım",
                        IsPersonal = true,
                        CreatedByUserId = user.Id
                    };
                    personalWorkspace.Members.Add(new WorkspaceMember { UserId = user.Id, Role = WorkspaceRole.Owner });
                    _context.Workspaces.Add(personalWorkspace);
                    await _context.SaveChangesAsync();

                    // State management: Inject the new WorkspaceId into the user's active session.
                    HttpContext.Session.SetInt32(ActiveWorkspaceService.SessionKey, personalWorkspace.Id);

                    // Authenticate the user bypassing the login screen and redirect to dashboard.
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return RedirectToPage("/Index");
                }

                // Bubble up ASP.NET Core Identity validation errors (e.g., weak passwords) to the UI.
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            
            // ModelState içinde hata varsa (Örn: KVKK işaretlenmediyse) sayfayı hatalarla geri döndür.
            return Page();
        }
    }
}