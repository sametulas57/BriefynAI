using System.ComponentModel.DataAnnotations;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingWeb.Pages.Auth
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly IPasswordResetService _service;

        public ResetPasswordModel(IPasswordResetService service) => _service = service;

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            public string UserId { get; set; } = string.Empty;

            [Required]
            public string Code { get; set; } = string.Empty;

            [Required(ErrorMessage = "Şifre alanı zorunludur.")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifreniz en az 6 karakter uzunluğunda olmalıdır.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
            [DataType(DataType.Password)]
            [Compare("Password", ErrorMessage = "Şifreler birbiriyle eşleşmiyor. Lütfen kontrol edin.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public bool LinkValid { get; private set; }

        public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers.CacheControl = "no-store";
        }

        public async Task<IActionResult> OnGetAsync(string? userId, string? code)
        {
            LinkValid = await _service.IsLinkValidAsync(userId, code);
            if (LinkValid)
            {
                Input.UserId = userId!;
                Input.Code = code!;
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                LinkValid = await _service.IsLinkValidAsync(Input.UserId, Input.Code);
                return Page();
            }

            var result = await _service.ResetAsync(Input.UserId, Input.Code, Input.Password);
            switch (result.Outcome)
            {
                case PasswordResetOutcome.Succeeded:
                    TempData["StatusMessage"] = "Şifreniz değişti. Yeni şifrenizle giriş yapın.";
                    return RedirectToPage("/Auth/Login");
                case PasswordResetOutcome.PasswordRejected:
                    foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
                    LinkValid = true;
                    return Page();
                default:
                    LinkValid = false;
                    return Page();
            }
        }
    }
}
