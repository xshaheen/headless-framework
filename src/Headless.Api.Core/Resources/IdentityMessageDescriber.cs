// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Humanizer;

#pragma warning disable CA1863 // Use 'CompositeFormat'
namespace Headless.Api.Resources;

[PublicAPI]
public static class IdentityMessageDescriber
{
    public static class Auth
    {
        public static ErrorDescriptor InvalidLoginIdentifier()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.InvalidLoginIdentifier,
                description: Messages.auth_invalid_login_identifier
            );
        }

        public static ErrorDescriptor InvalidToken()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.InvalidToken,
                description: Messages.auth_invalid_token
            );
        }

        public static ErrorDescriptor InvalidUserName(string userName)
        {
            var description = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_username, userName);

            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.InvalidUserName,
                description: description
            ).WithParam("UserName", userName);
        }

        public static ErrorDescriptor InvalidEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_email, email);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.InvalidEmail, description: desc).WithParam(
                "Email",
                email
            );
        }

        public static ErrorDescriptor InvalidRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_role, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.InvalidRole, description: desc).WithParam(
                "Name",
                role
            );
        }

        public static ErrorDescriptor DuplicateRoleName(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_role_name, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.DuplicateRoleName, description: desc).WithParam(
                "Name",
                role
            );
        }

        public static ErrorDescriptor UserAlreadyHasPassword()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.UserAlreadyHasPassword,
                description: Messages.auth_user_already_has_password
            );
        }

        public static ErrorDescriptor AlreadyInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_already_in_role, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.AlreadyInRole, description: desc).WithParam(
                "Name",
                role
            );
        }

        public static ErrorDescriptor RecoveryCodeRedemptionFailed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.RecoveryCodeRedemptionFailed,
                description: Messages.auth_recovery_code_redemption_failed
            );
        }

        public static ErrorDescriptor NotInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_not_in_role, role);

            return new ErrorDescriptor(code: IdentityErrorCodes.Auth.NotInRole, description: desc).WithParam(
                "Name",
                role
            );
        }

        public static ErrorDescriptor PasswordMismatch()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.PasswordMismatch,
                description: Messages.auth_password_mismatch
            );
        }

        public static ErrorDescriptor UserLockedOut()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.UserLockedOut,
                description: Messages.auth_user_locked_out
            );
        }

        public static ErrorDescriptor LoginRequiresConfirmation()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.LoginRequiresConfirmation,
                description: Messages.auth_login_requires_confirmation
            );
        }

        public static ErrorDescriptor LoginRequiresTwoFactor()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.LoginRequiresTwoFactor,
                description: Messages.auth_login_requires_two_factor
            );
        }

        public static ErrorDescriptor LoginFailed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Auth.LoginFailed,
                description: Messages.auth_login_failed
            );
        }
    }

    public static class Users
    {
        public static ErrorDescriptor DuplicatedPhoneNumber()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Users.DuplicatedPhoneNumber,
                description: Messages.user_duplicated_phone_number
            );
        }

        public static ErrorDescriptor DuplicatedNationalId()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Users.DuplicatedNationalId,
                description: Messages.user_duplicated_national_id
            );
        }

        public static ErrorDescriptor DuplicateUserName(string userName)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_username, userName);

            return new ErrorDescriptor(code: IdentityErrorCodes.Users.DuplicateUserName, description: desc).WithParam(
                "UserName",
                userName
            );
        }

        public static ErrorDescriptor DuplicateEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_email, email);

            return new ErrorDescriptor(code: IdentityErrorCodes.Users.DuplicateEmail, description: desc).WithParam(
                "Email",
                email
            );
        }

        public static ErrorDescriptor AlreadyBlocked()
        {
            return new ErrorDescriptor(IdentityErrorCodes.Users.AlreadyBlocked, Messages.user_already_blocked);
        }

        public static ErrorDescriptor AlreadyUnblocked()
        {
            return new ErrorDescriptor(IdentityErrorCodes.Users.AlreadyUnblocked, Messages.user_already_unblocked);
        }
    }

    public static class Passwords
    {
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

        public static ErrorDescriptor PasswordRequiresNonAlphanumeric()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresNonAlphanumeric,
                description: Messages.auth_password_requires_non_alphanumeric
            );
        }

        public static ErrorDescriptor PasswordRequiresDigit()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresDigit,
                description: Messages.auth_password_requires_digit
            );
        }

        public static ErrorDescriptor PasswordRequiresLower()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresLower,
                description: Messages.auth_password_requires_lower
            );
        }

        public static ErrorDescriptor PasswordRequiresUpper()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.PasswordRequiresUpper,
                description: Messages.auth_password_requires_upper
            );
        }

        public static ErrorDescriptor RemovePasswordRequiresAlternateLogin()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.RemovePasswordRequiresAlternateLogin,
                description: Messages.auth_remove_password_requires_alternate_login
            );
        }

        public static ErrorDescriptor UsedBeforePassword()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Passwords.UsedBeforePassword,
                description: Messages.auth_used_before_password
            );
        }

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

    public static class Emails
    {
        public static ErrorDescriptor AlreadyConfirmed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Emails.EmailAlreadyConfirmed,
                description: Messages.auth_email_already_confirmed
            );
        }

        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Emails.EmailAlreadyLinked,
                description: Messages.auth_email_already_linked
            );
        }

        public static ErrorDescriptor ExpiredToken()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Emails.ExpiredToken,
                description: Messages.auth_expired_token
            );
        }

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

    public static class PhoneNumbers
    {
        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(
                IdentityErrorCodes.PhoneNumbers.PhoneNumberAlreadyLinked,
                Messages.user_phone_number_already_linked
            );
        }

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

    public static class ExternalLogins
    {
        public static ErrorDescriptor LoginNotFound()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.LoginNotFound,
                description: Messages.auth_login_not_found
            );
        }

        public static ErrorDescriptor InvalidLoginToken()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.InvalidLoginToken,
                description: Messages.auth_invalid_login_token
            );
        }

        public static ErrorDescriptor LoginTokenValidationFailed()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.LoginTokenValidationFailed,
                description: Messages.auth_login_token_validation_failed
            );
        }

        public static ErrorDescriptor LoginAlreadyLinked()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.LoginAlreadyLinked,
                description: Messages.auth_login_already_linked
            );
        }

        public static ErrorDescriptor UserRequiresAlternateLoginOrPassword()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.ExternalLogins.UserRequiresAlternateLoginOrPassword,
                description: Messages.auth_user_requires_alternate_login_or_password
            );
        }
    }

    public static class Lockouts
    {
        public static ErrorDescriptor LockoutNotEnabled()
        {
            return new ErrorDescriptor(
                code: IdentityErrorCodes.Lockouts.LockoutNotEnabled,
                description: Messages.auth_lockout_not_enabled
            );
        }

        public static ErrorDescriptor AlreadyUnlocked()
        {
            return new ErrorDescriptor(IdentityErrorCodes.Lockouts.AlreadyUnlocked, Messages.user_already_unlocked);
        }
    }
}
