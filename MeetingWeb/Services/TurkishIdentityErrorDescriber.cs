using Microsoft.AspNetCore.Identity;

namespace MeetingWeb.Services;

public sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
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
