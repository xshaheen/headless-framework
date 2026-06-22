// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.IdentityErrors;

/// <summary>
/// <see cref="IdentityErrorDescriber"/> that returns <see cref="ParamsIdentityError"/> instances
/// whose <see cref="IdentityError.Code"/> values match the <see cref="IdentityErrorCodes"/> constants
/// and whose descriptions come from localized <see cref="IdentityMessageDescriber"/> factory methods.
/// Register this type via <c>builder.AddErrorDescriber&lt;HeadlessIdentityErrorDescriber&gt;()</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessIdentityErrorDescriber : IdentityErrorDescriber
{
    /// <inheritdoc/>
    public override IdentityError DefaultError()
    {
        return GeneralMessageDescriber.UnknownError().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError ConcurrencyFailure()
    {
        return GeneralMessageDescriber.ConcurrencyFailure().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError InvalidToken()
    {
        return IdentityMessageDescriber.Auth.InvalidToken().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError RecoveryCodeRedemptionFailed()
    {
        return IdentityMessageDescriber.Auth.RecoveryCodeRedemptionFailed().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError InvalidUserName(string? userName)
    {
        return IdentityMessageDescriber.Auth.InvalidUserName(userName ?? "").ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError InvalidEmail(string? email)
    {
        return IdentityMessageDescriber.Auth.InvalidEmail(email ?? "").ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError InvalidRoleName(string? role)
    {
        return IdentityMessageDescriber.Auth.InvalidRole(role ?? "").ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError DuplicateRoleName(string role)
    {
        return IdentityMessageDescriber.Auth.DuplicateRoleName(role).ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError UserAlreadyHasPassword()
    {
        return IdentityMessageDescriber.Auth.UserAlreadyHasPassword().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError UserAlreadyInRole(string role)
    {
        return IdentityMessageDescriber.Auth.AlreadyInRole(role).ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError UserNotInRole(string role)
    {
        return IdentityMessageDescriber.Auth.NotInRole(role).ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordMismatch()
    {
        return IdentityMessageDescriber.Auth.PasswordMismatch().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError DuplicateUserName(string? userName)
    {
        return IdentityMessageDescriber.Users.DuplicateUserName(userName ?? "").ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError DuplicateEmail(string email)
    {
        return IdentityMessageDescriber.Users.DuplicateEmail(email).ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError UserLockoutNotEnabled()
    {
        return IdentityMessageDescriber.Lockouts.LockoutNotEnabled().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError LoginAlreadyAssociated()
    {
        return IdentityMessageDescriber.ExternalLogins.LoginAlreadyLinked().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordTooShort(int length)
    {
        return IdentityMessageDescriber.Passwords.PasswordTooShort(length).ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresUniqueChars(uniqueChars).ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordRequiresNonAlphanumeric()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresNonAlphanumeric().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordRequiresDigit()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresDigit().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordRequiresLower()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresLower().ToIdentityError();
    }

    /// <inheritdoc/>
    public override IdentityError PasswordRequiresUpper()
    {
        return IdentityMessageDescriber.Passwords.PasswordRequiresUpper().ToIdentityError();
    }
}
