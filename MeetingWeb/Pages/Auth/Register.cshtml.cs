using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Pages.Auth
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ApplicationDbContext _context;

        // Constructor injection for Identity and EF Core dependencies.
        public RegisterModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
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
        }

        // HTTP POST handler for the registration workflow.
        public async Task<IActionResult> OnPostAsync()
        {
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
                var user = new IdentityUser { UserName = Input.Email, Email = Input.Email, PhoneNumber = Input.PhoneNumber };
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    // Inject FullName into the authentication token (Claims) for immediate UI rendering.
                    await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FullName", Input.FullName));

                    // ZERO-FRICTION ONBOARDING: Automatically provision a default isolated Workspace.
                    var personalWorkspace = new MeetingWeb.Models.Workspace
                    {
                        Name = "Kişisel Alanım"
                    };
                    _context.Workspaces.Add(personalWorkspace);
                    await _context.SaveChangesAsync(); // Commit to DB to generate primary key (Id).

                    // Assign the new user as the 'Owner' of their isolated workspace via the junction table.
                    var workspaceUser = new WorkspaceUser
                    {
                        WorkspaceId = personalWorkspace.Id,
                        UserId = user.Id,
                        Role = "Sahip"
                    };
                    _context.WorkspaceUsers.Add(workspaceUser);
                    await _context.SaveChangesAsync();

                    // State management: Inject the new WorkspaceId into the user's active session.
                    HttpContext.Session.SetInt32("ActiveWorkspaceId", personalWorkspace.Id);

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
            return Page();
        }
    }
}