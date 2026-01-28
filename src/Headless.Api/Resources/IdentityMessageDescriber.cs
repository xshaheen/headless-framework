// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Humanizer;

#pragma warning disable CA1863 // Use 'CompositeFormat'
namespace Headless.Api.Resources;

public static class IdentityMessageDescriber
{
    public static class Auth
    {
        public static ErrorDescriptor InvalidLoginIdentifier()
        {
            return new ErrorDescriptor(
                code: "auth:invalid_login_identifier",
                description: Messages.auth_invalid_login_identifier
            );
        }

        public static ErrorDescriptor InvalidToken()
        {
            return new ErrorDescriptor(code: "auth:invalid_token", description: Messages.auth_invalid_token);
        }

        public static ErrorDescriptor InvalidUserName(string userName)
        {
            var description = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_username, userName);

            return new ErrorDescriptor(code: "auth:invalid_username", description: description).WithParam(
                "UserName",
                userName
            );
        }

        public static ErrorDescriptor InvalidEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_email, email);

            return new ErrorDescriptor(code: "auth:invalid_email", description: desc).WithParam("Email", email);
        }

        public static ErrorDescriptor InvalidRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_role, role);

            return new ErrorDescriptor(code: "auth:invalid_role", description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor DuplicateRoleName(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_role_name, role);

            return new ErrorDescriptor(code: "auth:duplicate_role_name", description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor UserAlreadyHasPassword()
        {
            return new ErrorDescriptor(
                code: "auth:user_already_has_password",
                description: Messages.auth_user_already_has_password
            );
        }

        public static ErrorDescriptor AlreadyInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_already_in_role, role);

            return new ErrorDescriptor(code: "auth:already_in_role", description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor RecoveryCodeRedemptionFailed()
        {
            return new ErrorDescriptor(
                code: "auth:recovery_code_redemption_failed",
                description: Messages.auth_recovery_code_redemption_failed
            );
        }

        public static ErrorDescriptor NotInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_not_in_role, role);

            return new ErrorDescriptor(code: "auth:not_in_role", description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor PasswordMismatch()
        {
            return new ErrorDescriptor(code: "auth:password_mismatch", description: Messages.auth_password_mismatch);
        }

        public static ErrorDescriptor UserLockedOut()
        {
            return new ErrorDescriptor(code: "auth:user_locked_out", description: Messages.auth_user_locked_out);
        }

        public static ErrorDescriptor LoginRequiresConfirmation()
        {
            return new ErrorDescriptor(
                code: "auth:login_requires_confirmation",
                description: Messages.auth_login_requires_confirmation
            );
        }

        public static ErrorDescriptor LoginRequiresTwoFactor()
        {
            return new ErrorDescriptor(
                code: "auth:login_requires_two_factor",
                description: Messages.auth_login_requires_two_factor
            );
        }

        public static ErrorDescriptor LoginFailed()
        {
            return new ErrorDescriptor(code: "auth:login_failed", description: Messages.auth_login_failed);
        }
    }

    public static class Users
    {
        public static ErrorDescriptor DuplicatedPhoneNumber()
        {
            return new ErrorDescriptor(
                code: "user:duplicated_phone_number",
                description: Messages.user_duplicated_phone_number
            );
        }

        public static ErrorDescriptor DuplicatedNationalId()
        {
            return new ErrorDescriptor(
                code: "user:duplicated_national_id",
                description: Messages.user_duplicated_national_id
            );
        }

        public static ErrorDescriptor DuplicateUserName(string userName)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_username, userName);

            return new ErrorDescriptor(code: "auth:duplicate_username", description: desc).WithParam(
                "UserName",
                userName
            );
        }

        public static ErrorDescriptor DuplicateEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_email, email);

            return new ErrorDescriptor(code: "auth:duplicate_email", description: desc).WithParam("Email", email);
        }

        public static ErrorDescriptor AlreadyBlocked()
        {
            return new ErrorDescriptor("user:already_blocked", Messages.user_already_blocked);
        }

        public static ErrorDescriptor AlreadyUnblocked()
        {
            return new ErrorDescriptor("user:already_unblocked", Messages.user_already_unblocked);
        }
    }

    public static class Passwords
    {
        public static ErrorDescriptor PasswordTooShort(int length)
        {
            const string code = "auth:password_too_short";

            var desc = string.Format(
                CultureInfo.CurrentCulture,
                Messages.auth_password_too_short,
                length.ToString(provider: CultureInfo.CurrentCulture)
            );

            return new ErrorDescriptor(code: code, description: desc).WithParam("MinLength", length);
        }

        public static ErrorDescriptor PasswordRequiresUniqueChars(int uniqueChars)
        {
            var desc = string.Format(
                CultureInfo.CurrentCulture,
                Messages.auth_password_requires_unique_chars,
                uniqueChars.ToString(provider: CultureInfo.CurrentCulture)
            );

            return new ErrorDescriptor(code: "auth:password_requires_unique_chars", description: desc).WithParam(
                "UniqueChars",
                uniqueChars
            );
        }

        public static ErrorDescriptor PasswordRequiresNonAlphanumeric()
        {
            return new ErrorDescriptor(
                code: "auth:password_requires_non_alphanumeric",
                description: Messages.auth_password_requires_non_alphanumeric
            );
        }

        public static ErrorDescriptor PasswordRequiresDigit()
        {
            return new ErrorDescriptor(
                code: "auth:password_requires_digit",
                description: Messages.auth_password_requires_digit
            );
        }

        public static ErrorDescriptor PasswordRequiresLower()
        {
            return new ErrorDescriptor(
                code: "auth:password_requires_lower",
                description: Messages.auth_password_requires_lower
            );
        }

        public static ErrorDescriptor PasswordRequiresUpper()
        {
            return new ErrorDescriptor(
                code: "auth:password_requires_upper",
                description: Messages.auth_password_requires_upper
            );
        }

        public static ErrorDescriptor RemovePasswordRequiresAlternateLogin()
        {
            return new ErrorDescriptor(
                code: "auth:remove_password_requires_alternate_login",
                description: Messages.auth_remove_password_requires_alternate_login
            );
        }

        public static ErrorDescriptor UsedBeforePassword()
        {
            return new ErrorDescriptor(
                code: "auth:used_before_password",
                description: "You can't use a password that you have used before."
            );
        }

        public static ErrorDescriptor RequestForgetAttemptsExceeded(TimeSpan period, int maxAttempts)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_request_forget_password_attempts_exceeded,
                period.Humanize()
            );

            return new ErrorDescriptor("user:request_forget_password_attempts_exceeded", description)
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

            return new ErrorDescriptor("user:request_forget_password_cooldown", description)
                .WithParam("TimeUntilNextRequest", timeUntilNextRequest.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("TimeToCooldown", timeToCooldown.ToString("c", CultureInfo.InvariantCulture));
        }
    }

    public static class Emails
    {
        public static ErrorDescriptor AlreadyConfirmed()
        {
            return new ErrorDescriptor(
                code: "auth:email_already_confirmed",
                description: Messages.auth_email_already_confirmed
            );
        }

        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(
                code: "auth:email_already_linked",
                description: Messages.auth_email_already_linked
            );
        }

        public static ErrorDescriptor ExpiredToken()
        {
            return new ErrorDescriptor(code: "auth:expired_token", description: Messages.auth_expired_token);
        }

        public static ErrorDescriptor RequestVerifyCooldown(TimeSpan timeUntilNextRequest, TimeSpan timeToCooldown)
        {
            var description =
                $"You can only request to verify your email address once every {timeToCooldown.Humanize()}. Please wait {timeUntilNextRequest.Humanize()} before trying again.";

            return new ErrorDescriptor("user:request_verify_email_cooldown", description)
                .WithParam("TimeUntilNextRequest", timeUntilNextRequest.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("TimeToCooldown", timeToCooldown.ToString("c", CultureInfo.InvariantCulture));
        }

        public static ErrorDescriptor RequestVerifyAttemptsExceeded(TimeSpan period, int maxAttempts)
        {
            var description =
                $"You have exceeded the maximum number of attempts `{maxAttempts.ToString(CultureInfo.InvariantCulture)}` to verify your email address. Please try again after {period.Humanize()}.";

            return new ErrorDescriptor("user:request_verify_email_attempts_exceeded", description)
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

            return new ErrorDescriptor("user:email_already_linked_to_other_user", description);
        }
    }

    public static class PhoneNumbers
    {
        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(
                "user:phone_number_already_linked",
                "Phone number is already linked to the user."
            );
        }

        public static ErrorDescriptor AlreadyLinkedToOtherUser(string phoneNumber)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_phone_number_already_linked_to_other_user,
                phoneNumber
            );

            return new ErrorDescriptor("user:phone_number_already_linked_to_other_user", description);
        }
    }

    public static class ExternalLogins
    {
        public static ErrorDescriptor LoginNotFound()
        {
            return new ErrorDescriptor(code: "auth:login_not_found", description: Messages.auth_login_not_found);
        }

        public static ErrorDescriptor InvalidLoginToken()
        {
            return new ErrorDescriptor(
                code: "auth:invalid_login_token",
                description: Messages.auth_invalid_login_token
            );
        }

        public static ErrorDescriptor LoginTokenValidationFailed()
        {
            return new ErrorDescriptor(
                code: "auth:login_token_validation_failed",
                description: Messages.auth_login_token_validation_failed
            );
        }

        public static ErrorDescriptor LoginAlreadyLinked()
        {
            return new ErrorDescriptor(
                code: "auth:login_already_linked",
                description: Messages.auth_login_already_linked
            );
        }

        public static ErrorDescriptor UserRequiresAlternateLoginOrPassword()
        {
            return new ErrorDescriptor(
                code: "auth:user_requires_alternate_login_or_password",
                description: Messages.auth_user_requires_alternate_login_or_password
            );
        }
    }

    public static class Lockouts
    {
        public static ErrorDescriptor LockoutNotEnabled()
        {
            return new ErrorDescriptor(
                code: "auth:lockout_not_enabled",
                description: Messages.auth_lockout_not_enabled
            );
        }

        public static ErrorDescriptor AlreadyUnlocked()
        {
            return new ErrorDescriptor("user:already_unlocked", Messages.user_already_unlocked);
        }
    }
}
