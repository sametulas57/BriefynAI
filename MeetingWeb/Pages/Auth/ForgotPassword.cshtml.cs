using System.ComponentModel.DataAnnotations;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Pages.Auth
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly IPasswordResetService _service;
        private readonly AppOptions _appOptions;
        private readonly PasswordResetOptions _options;

        public ForgotPasswordModel(IPasswordResetService service, IOptions<AppOptions> appOptions,
            IOptions<PasswordResetOptions> options)
        {
            _service = service;
            _appOptions = appOptions.Value;
            _options = options.Value;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required(ErrorMessage = "E-posta alanı zorunludur.")]
            [EmailAddress(ErrorMessage = "Lütfen geçerli bir e-posta adresi giriniz.")]
            [StringLength(256)]
            public string Email { get; set; } = string.Empty;
        }

        public bool Sent { get; private set; }
        public string LinkLifetimeText => PasswordResetService.LifetimeText(_options.LinkLifetimeMinutes);

        public IActionResult OnGet(bool sent = false)
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Index");
            Sent = sent;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Index");
            if (!ModelState.IsValid) return Page();

            var baseUrl = PasswordResetLink.BaseUrl(_appOptions.PublicUrl, Request.Scheme, Request.Host);
            await _service.RequestAsync(Input.Email.Trim(), baseUrl);
            return RedirectToPage(new { sent = true });
        }
    }
}
