// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Humanizer;

#pragma warning disable CA1863 // Use 'CompositeFormat'
namespace Headless.Api.Resources;

/// <summary>
/// Factory methods that create <see cref="ErrorDescriptor"/> instances for identity-related error responses.
/// Each method returns a descriptor whose <see cref="ErrorDescriptor.Code"/> matches the corresponding
/// constant in <see cref="IdentityErrorCodes"/> and whose description is taken from the localized
/// <c>Messages</c> resource.
/// </summary>
[PublicAPI]
public static class IdentityMessageDescriber
{
    /// <summary>Authentication, token, role, and sign-in error descriptors. Codes follow the <c>auth:</c> prefix.</summary>
    public static class Auth
    {
        /// <summary>Returns a descriptor for an invalid login identifier (username or email) (<c>auth:invalid_login_identifier</c>).</summary>
        public static ErrorDescriptor InvalidLoginIdentifier()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.InvalidLoginIdentifier,
                description: Messages.auth_invalid_login_identifier
            );
        }

        /// <summary>Returns a descriptor for an invalid security or verification token (<c>auth:invalid_token</c>).</summary>
        public static ErrorDescriptor InvalidToken()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.InvalidToken,
                description: Messages.auth_invalid_token
            );
        }

        /// <summary>Returns a descriptor for an invalid username value (<c>auth:invalid_username</c>).</summary>
        /// <param name="userName">The invalid username embedded in the description.</param>
        public static ErrorDescriptor InvalidUserName(string userName)
        {
            var description = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_username, userName);

            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.InvalidUserName,
                description: description
            ).WithParam("UserName", userName);
        }

        /// <summary>Returns a descriptor for an invalid email address value (<c>auth:invalid_email</c>).</summary>
        /// <param name="email">The invalid email address embedded in the description.</param>
        public static ErrorDescriptor InvalidEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_email, email);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.InvalidEmail, description: desc).WithParam(
                "Email",
                email
            );
        }

        /// <summary>Returns a descriptor for an invalid role name (<c>auth:invalid_role</c>).</summary>
        /// <param name="role">The invalid role name embedded in the description.</param>
        public static ErrorDescriptor InvalidRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_role, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.InvalidRole, description: desc).WithParam(
                "Name",
                role
            );
        }

        /// <summary>Returns a descriptor for a duplicate role name (<c>auth:duplicate_role_name</c>).</summary>
        /// <param name="role">The conflicting role name embedded in the description.</param>
        public static ErrorDescriptor DuplicateRoleName(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_role_name, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.DuplicateRoleName, description: desc).WithParam(
                "Name",
                role
            );
        }

        /// <summary>Returns a descriptor for an attempt to set a password on a user who already has one (<c>auth:user_already_has_password</c>).</summary>
        public static ErrorDescriptor UserAlreadyHasPassword()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.UserAlreadyHasPassword,
                description: Messages.auth_user_already_has_password
            );
        }

        /// <summary>Returns a descriptor for adding a user to a role they already belong to (<c>auth:already_in_role</c>).</summary>
        /// <param name="role">The role name embedded in the description.</param>
        public static ErrorDescriptor AlreadyInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_already_in_role, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.AlreadyInRole, description: desc).WithParam(
                "Name",
                role
            );
        }

        /// <summary>Returns a descriptor for a failed multi-factor recovery code redemption (<c>auth:recovery_code_redemption_failed</c>).</summary>
        public static ErrorDescriptor RecoveryCodeRedemptionFailed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.RecoveryCodeRedemptionFailed,
                description: Messages.auth_recovery_code_redemption_failed
            );
        }

        /// <summary>Returns a descriptor for removing a user from a role they do not belong to (<c>auth:not_in_role</c>).</summary>
        /// <param name="role">The role name embedded in the description.</param>
        public static ErrorDescriptor NotInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_not_in_role, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.NotInRole, description: desc).WithParam(
                "Name",
                role
            );
        }

        /// <summary>Returns a descriptor for a current-password mismatch during password change (<c>auth:password_mismatch</c>).</summary>
        public static ErrorDescriptor PasswordMismatch()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.PasswordMismatch,
                description: Messages.auth_password_mismatch
            );
        }

        /// <summary>Returns a descriptor for a user who is currently locked out (<c>auth:user_locked_out</c>).</summary>
        public static ErrorDescriptor UserLockedOut()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.UserLockedOut,
                description: Messages.auth_user_locked_out
            );
        }

        /// <summary>Returns a descriptor for a login attempt that requires email confirmation first (<c>auth:login_requires_confirmation</c>).</summary>
        public static ErrorDescriptor LoginRequiresConfirmation()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.LoginRequiresConfirmation,
                description: Messages.auth_login_requires_confirmation
            );
        }

        /// <summary>Returns a descriptor for a login attempt that requires two-factor authentication (<c>auth:login_requires_two_factor</c>).</summary>
        public static ErrorDescriptor LoginRequiresTwoFactor()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.LoginRequiresTwoFactor,
                description: Messages.auth_login_requires_two_factor
            );
        }

        /// <summary>Returns a descriptor for a failed sign-in attempt due to invalid credentials (<c>auth:login_failed</c>).</summary>
        public static ErrorDescriptor LoginFailed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.LoginFailed,
                description: Messages.auth_login_failed
            );
        }
    }

    /// <summary>User-entity level error descriptors (duplicates, block/unblock, identifiers).</summary>
    public static class Users
    {
        /// <summary>Returns a descriptor for a duplicate phone number on the user entity (<c>user:duplicated_phone_number</c>).</summary>
        public static ErrorDescriptor DuplicatedPhoneNumber()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Users.DuplicatedPhoneNumber,
                description: Messages.user_duplicated_phone_number
            );
        }

        /// <summary>Returns a descriptor for a duplicate national ID on the user entity (<c>user:duplicated_national_id</c>).</summary>
        public static ErrorDescriptor DuplicatedNationalId()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Users.DuplicatedNationalId,
                description: Messages.user_duplicated_national_id
            );
        }

        /// <summary>Returns a descriptor for a duplicate username (<c>auth:duplicate_username</c>).</summary>
        /// <param name="userName">The conflicting username embedded in the description.</param>
        public static ErrorDescriptor DuplicateUserName(string userName)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_username, userName);

            return new ErrorDescriptor(code: IdentityErrorCodes.Users.DuplicateUserName, description: desc).WithParam(
                "UserName",
                userName
            );
        }

        /// <summary>Returns a descriptor for a duplicate email address (<c>auth:duplicate_email</c>).</summary>
        /// <param name="email">The conflicting email address embedded in the description.</param>
        public static ErrorDescriptor DuplicateEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_email, email);

            return new ErrorDescriptor(code: IdentityErrorCodes.Users.DuplicateEmail, description: desc).WithParam(
                "Email",
                email
            );
        }

        /// <summary>Returns a descriptor for an attempt to block a user who is already blocked (<c>user:already_blocked</c>).</summary>
        public static ErrorDescriptor AlreadyBlocked()
        {
            return new ErrorDescriptor(IdentityErrorCodes.Users.AlreadyBlocked, Messages.user_already_blocked);
        }

        /// <summary>Returns a descriptor for an attempt to unblock a user who is not blocked (<c>user:already_unblocked</c>).</summary>
        public static ErrorDescriptor AlreadyUnblocked()
        {
            return new ErrorDescriptor(IdentityErrorCodes.Users.AlreadyUnblocked, Messages.user_already_unblocked);
        }
    }

    /// <summary>Password strength and history violation error descriptors.</summary>
    public static class Passwords
    {
        /// <summary>Returns a descriptor for a password that is shorter than the minimum required length (<c>auth:password_too_short</c>).</summary>
        /// <param name="length">The minimum required character count embedded in the description.</param>
        public static ErrorDescriptor PasswordTooShort(int length)
        {
            var desc = string.Format(
                CultureInfo.CurrentCulture,
                Messages.auth_password_too_short,
                length.ToString(provider: CultureInfo.CurrentCulture)
            );

            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordTooShort,
                description: desc
            ).WithParam("MinLength", length);
        }

        /// <summary>Returns a descriptor for a password that does not contain enough unique characters (<c>auth:password_requires_unique_chars</c>).</summary>
        /// <param name="uniqueChars">The required number of unique characters embedded in the description.</param>
        public static ErrorDescriptor PasswordRequiresUniqueChars(int uniqueChars)
        {
            var desc = string.Format(
                CultureInfo.CurrentCulture,
                Messages.auth_password_requires_unique_chars,
                uniqueChars.ToString(provider: CultureInfo.CurrentCulture)
            );

            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresUniqueChars,
                description: desc
            ).WithParam("UniqueChars", uniqueChars);
        }

        /// <summary>Returns a descriptor for a password that does not contain a non-alphanumeric character (<c>auth:password_requires_non_alphanumeric</c>).</summary>
        public static ErrorDescriptor PasswordRequiresNonAlphanumeric()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresNonAlphanumeric,
                description: Messages.auth_password_requires_non_alphanumeric
            );
        }

        /// <summary>Returns a descriptor for a password that does not contain a digit (<c>auth:password_requires_digit</c>).</summary>
        public static ErrorDescriptor PasswordRequiresDigit()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresDigit,
                description: Messages.auth_password_requires_digit
            );
        }

        /// <summary>Returns a descriptor for a password that does not contain a lowercase letter (<c>auth:password_requires_lower</c>).</summary>
        public static ErrorDescriptor PasswordRequiresLower()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresLower,
                description: Messages.auth_password_requires_lower
            );
        }

        /// <summary>Returns a descriptor for a password that does not contain an uppercase letter (<c>auth:password_requires_upper</c>).</summary>
        public static ErrorDescriptor PasswordRequiresUpper()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresUpper,
                description: Messages.auth_password_requires_upper
            );
        }

        /// <summary>Returns a descriptor indicating the password cannot be removed because no alternate login exists (<c>auth:remove_password_requires_alternate_login</c>).</summary>
        public static ErrorDescriptor RemovePasswordRequiresAlternateLogin()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.RemovePasswordRequiresAlternateLogin,
                description: Messages.auth_remove_password_requires_alternate_login
            );
        }

        /// <summary>Returns a descriptor for a password that was used previously (<c>auth:used_before_password</c>).</summary>
        public static ErrorDescriptor UsedBeforePassword()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.UsedBeforePassword,
                description: Messages.auth_used_before_password
            );
        }

        /// <summary>Returns a descriptor for exceeded forgot-password request attempts within a rolling period (<c>user:request_forget_password_attempts_exceeded</c>).</summary>
        /// <param name="period">The rolling window during which attempts are counted; embedded in the description.</param>
        /// <param name="maxAttempts">The maximum allowed attempts within <paramref name="period"/>; attached as a named param.</param>
        public static ErrorDescriptor RequestForgetAttemptsExceeded(TimeSpan period, int maxAttempts)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_request_forget_password_attempts_exceeded,
                period.Humanize()
            );

            return new ErrorDescriptor(IdentityErrorCodes.Passwords.RequestForgetPasswordAttemptsExceeded, description)
                .WithParam("Period", period.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("MaxAttempts", maxAttempts.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Returns a descriptor for a forgot-password request that is still within the cooldown window (<c>user:request_forget_password_cooldown</c>).</summary>
        /// <param name="timeUntilNextRequest">Remaining cooldown time until the user may try again; embedded in the description.</param>
        /// <param name="timeToCooldown">Total cooldown duration; attached as a named param.</param>
        public static ErrorDescriptor RequestForgetCooldown(TimeSpan timeUntilNextRequest, TimeSpan timeToCooldown)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_request_forget_password_cooldown,
                timeToCooldown.Humanize(),
                timeUntilNextRequest.Humanize()
            );

            return new ErrorDescriptor(IdentityErrorCodes.Passwords.RequestForgetPasswordCooldown, description)
                .WithParam("TimeUntilNextRequest", timeUntilNextRequest.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("TimeToCooldown", timeToCooldown.ToString("c", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Email confirmation and verification error descriptors.</summary>
    public static class Emails
    {
        /// <summary>Returns a descriptor for an email address that is already confirmed (<c>auth:email_already_confirmed</c>).</summary>
        public static ErrorDescriptor AlreadyConfirmed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Emails.EmailAlreadyConfirmed,
                description: Messages.auth_email_already_confirmed
            );
        }

        /// <summary>Returns a descriptor for an email address that is already linked to the current user (<c>auth:email_already_linked</c>).</summary>
        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Emails.EmailAlreadyLinked,
                description: Messages.auth_email_already_linked
            );
        }

        /// <summary>Returns a descriptor for an expired email verification or confirmation token (<c>auth:expired_token</c>).</summary>
        public static ErrorDescriptor ExpiredToken()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Emails.ExpiredToken,
                description: Messages.auth_expired_token
            );
        }

        /// <summary>Returns a descriptor for an email verification request that is still within the cooldown window (<c>user:request_verify_email_cooldown</c>).</summary>
        /// <param name="timeUntilNextRequest">Remaining cooldown time; embedded in the description.</param>
        /// <param name="timeToCooldown">Total cooldown duration; attached as a named param.</param>
        public static ErrorDescriptor RequestVerifyCooldown(TimeSpan timeUntilNextRequest, TimeSpan timeToCooldown)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_request_verify_email_cooldown,
                timeToCooldown.Humanize(),
                timeUntilNextRequest.Humanize()
            );

            return new ErrorDescriptor(IdentityErrorCodes.Emails.RequestVerifyEmailCooldown, description)
                .WithParam("TimeUntilNextRequest", timeUntilNextRequest.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("TimeToCooldown", timeToCooldown.ToString("c", CultureInfo.InvariantCulture));
        }

        /// <summary>Returns a descriptor for exceeded email verification request attempts within a rolling period (<c>user:request_verify_email_attempts_exceeded</c>).</summary>
        /// <param name="period">The rolling window during which attempts are counted; embedded in the description.</param>
        /// <param name="maxAttempts">The maximum allowed attempts within <paramref name="period"/>; attached as a named param.</param>
        public static ErrorDescriptor RequestVerifyAttemptsExceeded(TimeSpan period, int maxAttempts)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_request_verify_email_attempts_exceeded,
                maxAttempts.ToString(CultureInfo.InvariantCulture),
                period.Humanize()
            );

            return new ErrorDescriptor(IdentityErrorCodes.Emails.RequestVerifyEmailAttemptsExceeded, description)
                .WithParam("Period", period.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("MaxAttempts", maxAttempts);
        }

        /// <summary>Returns a descriptor for an email address that is already linked to a different user (<c>user:email_already_linked_to_other_user</c>).</summary>
        /// <param name="newEmail">The email address that is already taken; embedded in the description.</param>
        public static ErrorDescriptor AlreadyLinkedToOtherUser(string newEmail)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_email_already_linked_to_other_user,
                newEmail
            );

            return new ErrorDescriptor(IdentityErrorCodes.Emails.EmailAlreadyLinkedToOtherUser, description);
        }
    }

    /// <summary>Phone number linking error descriptors.</summary>
    public static class PhoneNumbers
    {
        /// <summary>Returns a descriptor for a phone number that is already linked to the current user (<c>user:phone_number_already_linked</c>).</summary>
        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(
                IdentityErrorCodes.PhoneNumbers.PhoneNumberAlreadyLinked,
                Messages.user_phone_number_already_linked
            );
        }

        /// <summary>Returns a descriptor for a phone number that is already linked to a different user (<c>user:phone_number_already_linked_to_other_user</c>).</summary>
        /// <param name="phoneNumber">The phone number that is already taken; embedded in the description.</param>
        public static ErrorDescriptor AlreadyLinkedToOtherUser(string phoneNumber)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_phone_number_already_linked_to_other_user,
                phoneNumber
            );

            return new ErrorDescriptor(
                IdentityErrorCodes.PhoneNumbers.PhoneNumberAlreadyLinkedToOtherUser,
                description
            );
        }
    }

    /// <summary>External (OAuth/OIDC) login linking and validation error descriptors.</summary>
    public static class ExternalLogins
    {
        /// <summary>Returns a descriptor for an external login that could not be found (<c>auth:login_not_found</c>).</summary>
        public static ErrorDescriptor LoginNotFound()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.LoginNotFound,
                description: Messages.auth_login_not_found
            );
        }

        /// <summary>Returns a descriptor for an invalid or tampered external login token (<c>auth:invalid_login_token</c>).</summary>
        public static ErrorDescriptor InvalidLoginToken()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.InvalidLoginToken,
                description: Messages.auth_invalid_login_token
            );
        }

        /// <summary>Returns a descriptor for a failed external login token validation (<c>auth:login_token_validation_failed</c>).</summary>
        public static ErrorDescriptor LoginTokenValidationFailed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.LoginTokenValidationFailed,
                description: Messages.auth_login_token_validation_failed
            );
        }

        /// <summary>Returns a descriptor for an external login that is already linked to the current user (<c>auth:login_already_linked</c>).</summary>
        public static ErrorDescriptor LoginAlreadyLinked()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.LoginAlreadyLinked,
                description: Messages.auth_login_already_linked
            );
        }

        /// <summary>Returns a descriptor for a user who must keep at least one alternate login or password before removing the current external login (<c>auth:user_requires_alternate_login_or_password</c>).</summary>
        public static ErrorDescriptor UserRequiresAlternateLoginOrPassword()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.UserRequiresAlternateLoginOrPassword,
                description: Messages.auth_user_requires_alternate_login_or_password
            );
        }
    }

    /// <summary>Lockout management error descriptors.</summary>
    public static class Lockouts
    {
        /// <summary>Returns a descriptor for a lockout operation that failed because lockout is not enabled for the user store (<c>auth:lockout_not_enabled</c>).</summary>
        public static ErrorDescriptor LockoutNotEnabled()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Lockouts.LockoutNotEnabled,
                description: Messages.auth_lockout_not_enabled
            );
        }

        /// <summary>Returns a descriptor for an attempt to unlock a user who is not currently locked out (<c>user:already_unlocked</c>).</summary>
        public static ErrorDescriptor AlreadyUnlocked()
        {
            return new ErrorDescriptor(IdentityErrorCodes.Lockouts.AlreadyUnlocked, Messages.user_already_unlocked);
        }
    }
}
