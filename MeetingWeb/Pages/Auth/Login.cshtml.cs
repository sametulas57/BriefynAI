using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace MeetingWeb.Pages.Auth
{
    // Allow unauthenticated access to the login page.
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required(ErrorMessage = "E-posta alanı zorunludur.")]
            [EmailAddress(ErrorMessage = "Lütfen geçerli bir e-posta adresi giriniz.")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Şifre alanı zorunludur.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            public bool RememberMe { get; set; }
        }

        // HTTP GET handler for the login page.
        public IActionResult OnGet(string? returnUrl = null)
        {
            // Redirect authenticated users away from the login page.
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToPage("/Index");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return Page();
        }

        // HTTP POST handler for authentication submission.
        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            // Set default redirect URL to application root.
            returnUrl ??= Url.Content("~/");

            if (ModelState.IsValid)
            {
                // Authenticate using ASP.NET Core Identity SignInManager.
                var result = await _signInManager.PasswordSignInAsync(
                    Input.Email,
                    Input.Password,
                    Input.RememberMe,
                    lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    // SECURITY: Use LocalRedirect to prevent Open Redirect vulnerabilities.
                    return LocalRedirect(returnUrl);
                }
                else
                {
                    // SECURITY: Generic error message to prevent User Enumeration attacks.
                    ModelState.AddModelError(string.Empty, "Geçersiz giriş denemesi. Lütfen e-posta ve şifrenizi kontrol edin.");
                    return Page();
                }
            }

            return Page();
        }
    }
}