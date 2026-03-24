// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Humanizer;

#pragma warning disable CA1863 // Use 'CompositeFormat'
namespace Headless.Api.Resources;

public static class IdentityMessageDescriber
{
    public static class Auth
    {
        private const string AlreadyInRoleCode = "auth:already_in_role";
        private const string DuplicateRoleNameCode = "auth:duplicate_role_name";
        private const string InvalidLoginIdentifierCode = "auth:invalid_login_identifier";
        private const string InvalidTokenCode = "auth:invalid_token";
        private const string InvalidUserNameCode = "auth:invalid_username";
        private const string InvalidEmailCode = "auth:invalid_email";
        private const string InvalidRoleCode = "auth:invalid_role";
        private const string NotInRoleCode = "auth:not_in_role";
        private const string RecoveryCodeRedemptionFailedCode = "auth:recovery_code_redemption_failed";
        private const string UserAlreadyHasPasswordCode = "auth:user_already_has_password";
        private const string PasswordMismatchCode = "auth:password_mismatch";
        private const string UserLockedOutCode = "auth:user_locked_out";
        private const string LoginRequiresConfirmationCode = "auth:login_requires_confirmation";
        private const string LoginRequiresTwoFactorCode = "auth:login_requires_two_factor";
        private const string LoginFailedCode = "auth:login_failed";

        public static ErrorDescriptor InvalidLoginIdentifier()
        {
            return new ErrorDescriptor(
                code: InvalidLoginIdentifierCode,
                description: Messages.auth_invalid_login_identifier
            );
        }

        public static ErrorDescriptor InvalidToken()
        {
            return new ErrorDescriptor(code: InvalidTokenCode, description: Messages.auth_invalid_token);
        }

        public static ErrorDescriptor InvalidUserName(string userName)
        {
            var description = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_username, userName);

            return new ErrorDescriptor(code: InvalidUserNameCode, description: description).WithParam(
                "UserName",
                userName
            );
        }

        public static ErrorDescriptor InvalidEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_email, email);

            return new ErrorDescriptor(code: InvalidEmailCode, description: desc).WithParam("Email", email);
        }

        public static ErrorDescriptor InvalidRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_invalid_role, role);

            return new ErrorDescriptor(code: InvalidRoleCode, description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor DuplicateRoleName(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_role_name, role);

            return new ErrorDescriptor(code: DuplicateRoleNameCode, description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor UserAlreadyHasPassword()
        {
            return new ErrorDescriptor(
                code: UserAlreadyHasPasswordCode,
                description: Messages.auth_user_already_has_password
            );
        }

        public static ErrorDescriptor AlreadyInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_already_in_role, role);

            return new ErrorDescriptor(code: AlreadyInRoleCode, description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor RecoveryCodeRedemptionFailed()
        {
            return new ErrorDescriptor(
                code: RecoveryCodeRedemptionFailedCode,
                description: Messages.auth_recovery_code_redemption_failed
            );
        }

        public static ErrorDescriptor NotInRole(string role)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_not_in_role, role);

            return new ErrorDescriptor(code: NotInRoleCode, description: desc).WithParam("Name", role);
        }

        public static ErrorDescriptor PasswordMismatch()
        {
            return new ErrorDescriptor(code: PasswordMismatchCode, description: Messages.auth_password_mismatch);
        }

        public static ErrorDescriptor UserLockedOut()
        {
            return new ErrorDescriptor(code: UserLockedOutCode, description: Messages.auth_user_locked_out);
        }

        public static ErrorDescriptor LoginRequiresConfirmation()
        {
            return new ErrorDescriptor(
                code: LoginRequiresConfirmationCode,
                description: Messages.auth_login_requires_confirmation
            );
        }

        public static ErrorDescriptor LoginRequiresTwoFactor()
        {
            return new ErrorDescriptor(
                code: LoginRequiresTwoFactorCode,
                description: Messages.auth_login_requires_two_factor
            );
        }

        public static ErrorDescriptor LoginFailed()
        {
            return new ErrorDescriptor(code: LoginFailedCode, description: Messages.auth_login_failed);
        }
    }

    public static class Users
    {
        private const string DuplicateUserNameCode = "auth:duplicate_username";
        private const string DuplicateEmailCode = "auth:duplicate_email";
        private const string AlreadyBlockedCode = "user:already_blocked";
        private const string AlreadyUnblockedCode = "user:already_unblocked";
        private const string DuplicatedNationalIdCode = "user:duplicated_national_id";
        private const string DuplicatedPhoneNumberCode = "user:duplicated_phone_number";

        public static ErrorDescriptor DuplicatedPhoneNumber()
        {
            return new ErrorDescriptor(
                code: DuplicatedPhoneNumberCode,
                description: Messages.user_duplicated_phone_number
            );
        }

        public static ErrorDescriptor DuplicatedNationalId()
        {
            return new ErrorDescriptor(
                code: DuplicatedNationalIdCode,
                description: Messages.user_duplicated_national_id
            );
        }

        public static ErrorDescriptor DuplicateUserName(string userName)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_username, userName);

            return new ErrorDescriptor(code: DuplicateUserNameCode, description: desc).WithParam("UserName", userName);
        }

        public static ErrorDescriptor DuplicateEmail(string email)
        {
            var desc = string.Format(CultureInfo.CurrentCulture, Messages.auth_duplicate_email, email);

            return new ErrorDescriptor(code: DuplicateEmailCode, description: desc).WithParam("Email", email);
        }

        public static ErrorDescriptor AlreadyBlocked()
        {
            return new ErrorDescriptor(AlreadyBlockedCode, Messages.user_already_blocked);
        }

        public static ErrorDescriptor AlreadyUnblocked()
        {
            return new ErrorDescriptor(AlreadyUnblockedCode, Messages.user_already_unblocked);
        }
    }

    public static class Passwords
    {
        private const string PasswordTooShortCode = "auth:password_too_short";
        private const string PasswordRequiresUniqueCharsCode = "auth:password_requires_unique_chars";
        private const string PasswordRequiresNonAlphanumericCode = "auth:password_requires_non_alphanumeric";
        private const string PasswordRequiresDigitCode = "auth:password_requires_digit";
        private const string PasswordRequiresLowerCode = "auth:password_requires_lower";
        private const string PasswordRequiresUpperCode = "auth:password_requires_upper";
        private const string RemovePasswordRequiresAlternateLoginCode = "auth:remove_password_requires_alternate_login";
        private const string UsedBeforePasswordCode = "auth:used_before_password";
        private const string RequestForgetPasswordAttemptsExceededCode =
            "user:request_forget_password_attempts_exceeded";
        private const string RequestForgetPasswordCooldownCode = "user:request_forget_password_cooldown";

        public static ErrorDescriptor PasswordTooShort(int length)
        {
            var desc = string.Format(
                CultureInfo.CurrentCulture,
                Messages.auth_password_too_short,
                length.ToString(provider: CultureInfo.CurrentCulture)
            );

            return new ErrorDescriptor(code: PasswordTooShortCode, description: desc).WithParam("MinLength", length);
        }

        public static ErrorDescriptor PasswordRequiresUniqueChars(int uniqueChars)
        {
            var desc = string.Format(
                CultureInfo.CurrentCulture,
                Messages.auth_password_requires_unique_chars,
                uniqueChars.ToString(provider: CultureInfo.CurrentCulture)
            );

            return new ErrorDescriptor(code: PasswordRequiresUniqueCharsCode, description: desc).WithParam(
                "UniqueChars",
                uniqueChars
            );
        }

        public static ErrorDescriptor PasswordRequiresNonAlphanumeric()
        {
            return new ErrorDescriptor(
                code: PasswordRequiresNonAlphanumericCode,
                description: Messages.auth_password_requires_non_alphanumeric
            );
        }

        public static ErrorDescriptor PasswordRequiresDigit()
        {
            return new ErrorDescriptor(
                code: PasswordRequiresDigitCode,
                description: Messages.auth_password_requires_digit
            );
        }

        public static ErrorDescriptor PasswordRequiresLower()
        {
            return new ErrorDescriptor(
                code: PasswordRequiresLowerCode,
                description: Messages.auth_password_requires_lower
            );
        }

        public static ErrorDescriptor PasswordRequiresUpper()
        {
            return new ErrorDescriptor(
                code: PasswordRequiresUpperCode,
                description: Messages.auth_password_requires_upper
            );
        }

        public static ErrorDescriptor RemovePasswordRequiresAlternateLogin()
        {
            return new ErrorDescriptor(
                code: RemovePasswordRequiresAlternateLoginCode,
                description: Messages.auth_remove_password_requires_alternate_login
            );
        }

        public static ErrorDescriptor UsedBeforePassword()
        {
            return new ErrorDescriptor(
                code: UsedBeforePasswordCode,
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

            return new ErrorDescriptor(RequestForgetPasswordAttemptsExceededCode, description)
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

            return new ErrorDescriptor(RequestForgetPasswordCooldownCode, description)
                .WithParam("TimeUntilNextRequest", timeUntilNextRequest.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("TimeToCooldown", timeToCooldown.ToString("c", CultureInfo.InvariantCulture));
        }
    }

    public static class Emails
    {
        private const string EmailAlreadyConfirmedCode = "auth:email_already_confirmed";
        private const string EmailAlreadyLinkedCode = "auth:email_already_linked";
        private const string ExpiredTokenCode = "auth:expired_token";
        private const string RequestVerifyEmailCooldownCode = "user:request_verify_email_cooldown";
        private const string RequestVerifyEmailAttemptsExceededCode = "user:request_verify_email_attempts_exceeded";
        private const string EmailAlreadyLinkedToOtherUserCode = "user:email_already_linked_to_other_user";

        public static ErrorDescriptor AlreadyConfirmed()
        {
            return new ErrorDescriptor(
                code: EmailAlreadyConfirmedCode,
                description: Messages.auth_email_already_confirmed
            );
        }

        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(code: EmailAlreadyLinkedCode, description: Messages.auth_email_already_linked);
        }

        public static ErrorDescriptor ExpiredToken()
        {
            return new ErrorDescriptor(code: ExpiredTokenCode, description: Messages.auth_expired_token);
        }

        public static ErrorDescriptor RequestVerifyCooldown(TimeSpan timeUntilNextRequest, TimeSpan timeToCooldown)
        {
            var description =
                $"You can only request to verify your email address once every {timeToCooldown.Humanize()}. Please wait {timeUntilNextRequest.Humanize()} before trying again.";

            return new ErrorDescriptor(RequestVerifyEmailCooldownCode, description)
                .WithParam("TimeUntilNextRequest", timeUntilNextRequest.ToString("c", CultureInfo.InvariantCulture))
                .WithParam("TimeToCooldown", timeToCooldown.ToString("c", CultureInfo.InvariantCulture));
        }

        public static ErrorDescriptor RequestVerifyAttemptsExceeded(TimeSpan period, int maxAttempts)
        {
            var description =
                $"You have exceeded the maximum number of attempts `{maxAttempts.ToString(CultureInfo.InvariantCulture)}` to verify your email address. Please try again after {period.Humanize()}.";

            return new ErrorDescriptor(RequestVerifyEmailAttemptsExceededCode, description)
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

            return new ErrorDescriptor(EmailAlreadyLinkedToOtherUserCode, description);
        }
    }

    public static class PhoneNumbers
    {
        private const string PhoneNumberAlreadyLinkedCode = "user:phone_number_already_linked";
        private const string PhoneNumberAlreadyLinkedToOtherUserCode = "user:phone_number_already_linked_to_other_user";

        public static ErrorDescriptor AlreadyLinked()
        {
            return new ErrorDescriptor(PhoneNumberAlreadyLinkedCode, "Phone number is already linked to the user.");
        }

        public static ErrorDescriptor AlreadyLinkedToOtherUser(string phoneNumber)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                Messages.user_phone_number_already_linked_to_other_user,
                phoneNumber
            );

            return new ErrorDescriptor(PhoneNumberAlreadyLinkedToOtherUserCode, description);
        }
    }

    public static class ExternalLogins
    {
        private const string LoginNotFoundCode = "auth:login_not_found";
        private const string InvalidLoginTokenCode = "auth:invalid_login_token";
        private const string LoginTokenValidationFailedCode = "auth:login_token_validation_failed";
        private const string LoginAlreadyLinkedCode = "auth:login_already_linked";
        private const string UserRequiresAlternateLoginOrPasswordCode =
            "auth:user_requires_alternate_login_or_password";

        public static ErrorDescriptor LoginNotFound()
        {
            return new ErrorDescriptor(code: LoginNotFoundCode, description: Messages.auth_login_not_found);
        }

        public static ErrorDescriptor InvalidLoginToken()
        {
            return new ErrorDescriptor(code: InvalidLoginTokenCode, description: Messages.auth_invalid_login_token);
        }

        public static ErrorDescriptor LoginTokenValidationFailed()
        {
            return new ErrorDescriptor(
                code: LoginTokenValidationFailedCode,
                description: Messages.auth_login_token_validation_failed
            );
        }

        public static ErrorDescriptor LoginAlreadyLinked()
        {
            return new ErrorDescriptor(code: LoginAlreadyLinkedCode, description: Messages.auth_login_already_linked);
        }

        public static ErrorDescriptor UserRequiresAlternateLoginOrPassword()
        {
            return new ErrorDescriptor(
                code: UserRequiresAlternateLoginOrPasswordCode,
                description: Messages.auth_user_requires_alternate_login_or_password
            );
        }
    }

    public static class Lockouts
    {
        private const string LockoutNotEnabledCode = "auth:lockout_not_enabled";
        private const string AlreadyUnlockedCode = "user:already_unlocked";

        public static ErrorDescriptor LockoutNotEnabled()
        {
            return new ErrorDescriptor(code: LockoutNotEnabledCode, description: Messages.auth_lockout_not_enabled);
        }

        public static ErrorDescriptor AlreadyUnlocked()
        {
            return new ErrorDescriptor(AlreadyUnlockedCode, Messages.user_already_unlocked);
        }
    }
}
