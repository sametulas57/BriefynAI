using Microsoft.AspNetCore.Identity;

namespace MeetingWeb.Services;

public sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError ConcurrencyFailure() =>
        WithDescription(base.ConcurrencyFailure(), "Bu işlem aynı anda başka bir yerden yapıldı. Sayfayı yenileyip tekrar deneyin.");

    public override IdentityError DefaultError() =>
        WithDescription(base.DefaultError(), "Bir şeyler ters gitti. Lütfen tekrar deneyin.");

    public override IdentityError DuplicateUserName(string userName) =>
        WithDescription(base.DuplicateUserName(userName), "Bu e-posta adresiyle zaten bir hesap var. Giriş yapabilir ya da şifrenizi yenileyebilirsiniz.");

    public override IdentityError DuplicateEmail(string email) =>
        WithDescription(base.DuplicateEmail(email), "Bu e-posta adresiyle zaten bir hesap var. Giriş yapabilir ya da şifrenizi yenileyebilirsiniz.");

    public override IdentityError InvalidUserName(string? userName) =>
        WithDescription(base.InvalidUserName(userName), "Geçerli bir e-posta adresi yazın.");

    public override IdentityError InvalidEmail(string? email) =>
        WithDescription(base.InvalidEmail(email), "Geçerli bir e-posta adresi yazın.");

    public override IdentityError PasswordTooShort(int length) =>
        WithDescription(base.PasswordTooShort(length), $"Şifreniz en az {length} karakter olmalı.");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        WithDescription(base.PasswordRequiresNonAlphanumeric(), "Şifrenizde en az bir işaret (ör. ! ya da *) olmalı.");

    public override IdentityError PasswordRequiresDigit() =>
        WithDescription(base.PasswordRequiresDigit(), "Şifrenizde en az bir rakam olmalı.");

    public override IdentityError PasswordRequiresLower() =>
        WithDescription(base.PasswordRequiresLower(), "Şifrenizde en az bir küçük harf olmalı.");

    public override IdentityError PasswordRequiresUpper() =>
        WithDescription(base.PasswordRequiresUpper(), "Şifrenizde en az bir büyük harf olmalı.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        WithDescription(base.PasswordRequiresUniqueChars(uniqueChars), $"Şifrenizde en az {uniqueChars} farklı karakter olmalı.");

    public override IdentityError InvalidToken() =>
        WithDescription(base.InvalidToken(), "Bu bağlantı artık geçerli değil.");

    private static IdentityError WithDescription(IdentityError error, string description)
    {
        error.Description = description;
        return error;
    }
}
