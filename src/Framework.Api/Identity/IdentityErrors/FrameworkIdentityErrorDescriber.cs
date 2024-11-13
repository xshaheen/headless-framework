// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Resources;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.IdentityErrors;

public sealed class FrameworkIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError()
    {
        return GeneralMessageDescriber.UnknownError().ToIdentityError();
    }

    public override IdentityError ConcurrencyFailure()
    {
        return GeneralMessageDescriber.ConcurrencyFailure().ToIdentityError();
    }

    public override IdentityError InvalidToken()
    {
        return IdentityMessageDescriber.Auth.InvalidToken().ToIdentityError();
    }

    public override IdentityError RecoveryCodeRedemptionFailed()
    {
        return IdentityMessageDescriber.Auth.RecoveryCodeRedemptionFailed().ToIdentityError();
    }

    public override IdentityError InvalidUserName(string? userName)
    {
        return IdentityMessageDescriber.Auth.InvalidUserName(userName ?? "").ToIdentityError();
    }

    public override IdentityError InvalidEmail(string? email)
    {
        return IdentityMessageDescriber.Auth.InvalidEmail(email ?? "").ToIdentityError();
    }

    public override IdentityError InvalidRoleName(string? role)
    {
        return IdentityMessageDescriber.Auth.InvalidRole(role ?? "").ToIdentityError();
    }

    public override IdentityError DuplicateRoleName(string role)
    {
        return IdentityMessageDescriber.Auth.DuplicateRoleName(role).ToIdentityError();
    }

    public override IdentityError UserAlreadyHasPassword()
    {
        return IdentityMessageDescriber.Auth.UserAlreadyHasPassword().ToIdentityError();
    }

    public override IdentityError UserAlreadyInRole(string role)
    {
        return IdentityMessageDescriber.Auth.AlreadyInRole(role).ToIdentityError();
    }

    public override IdentityError UserNotInRole(string role)
    {
        return IdentityMessageDescriber.Auth.NotInRole(role).ToIdentityError();
    }

    public override IdentityError PasswordMismatch()
    {
        return IdentityMessageDescriber.Auth.PasswordMismatch().ToIdentityError();
    }

    public override IdentityError DuplicateUserName(string? userName)
    {
        return IdentityMessageDescriber.Users.DuplicateUserName(userName ?? "").ToIdentityError();
    }

    public override IdentityError DuplicateEmail(string email)
    {
        return IdentityMessageDescriber.Users.DuplicateEmail(email).ToIdentityError();
    }

    public override IdentityError UserLockoutNotEnabled()
    {
        return IdentityMessageDescriber.Lockouts.LockoutNotEnabled().ToIdentityError();
    }

    public override IdentityError LoginAlreadyAssociated()
    {
        return IdentityMessageDescriber.ExternalLogins.LoginAlreadyLinked().ToIdentityError();
    }

    public override IdentityError PasswordTooShort(int length)
    {
        return IdentityMessageDescriber.Passwords.PasswordTooShort(length).ToIdentityError();
    }

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresUniqueChars(uniqueChars).ToIdentityError();
    }

    public override IdentityError PasswordRequiresNonAlphanumeric()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresNonAlphanumeric().ToIdentityError();
    }

    public override IdentityError PasswordRequiresDigit()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresDigit().ToIdentityError();
    }

    public override IdentityError PasswordRequiresLower()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresLower().ToIdentityError();
    }

    public override IdentityError PasswordRequiresUpper()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresUpper().ToIdentityError();
    }
}
