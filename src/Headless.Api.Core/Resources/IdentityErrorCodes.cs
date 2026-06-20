// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Resources;

/// <summary>
/// Compile-time constants for identity-related <c>errors[].code</c> values in ProblemDetails responses.
/// All codes follow the <c>prefix:snake_case</c> shape (<c>auth:</c> for auth/role/password errors,
/// <c>user:</c> for user-entity errors).
/// </summary>
[PublicAPI]
public static class IdentityErrorCodes
{
    /// <summary>Authentication, token, role, and sign-in failure codes.</summary>
    public static class Auth
    {
        public const string AlreadyInRole = "auth:already_in_role";
        public const string DuplicateRoleName = "auth:duplicate_role_name";
        public const string InvalidLoginIdentifier = "auth:invalid_login_identifier";
        public const string InvalidToken = "auth:invalid_token";
        public const string InvalidUserName = "auth:invalid_username";
        public const string InvalidEmail = "auth:invalid_email";
        public const string InvalidRole = "auth:invalid_role";
        public const string NotInRole = "auth:not_in_role";
        public const string RecoveryCodeRedemptionFailed = "auth:recovery_code_redemption_failed";
        public const string UserAlreadyHasPassword = "auth:user_already_has_password";
        public const string PasswordMismatch = "auth:password_mismatch";
        public const string UserLockedOut = "auth:user_locked_out";
        public const string LoginRequiresConfirmation = "auth:login_requires_confirmation";
        public const string LoginRequiresTwoFactor = "auth:login_requires_two_factor";
        public const string LoginFailed = "auth:login_failed";
    }

    /// <summary>User-entity level error codes (duplicates, block/unblock, identifiers).</summary>
    public static class Users
    {
        public const string DuplicateUserName = "auth:duplicate_username";
        public const string DuplicateEmail = "auth:duplicate_email";
        public const string AlreadyBlocked = "user:already_blocked";
        public const string AlreadyUnblocked = "user:already_unblocked";
        public const string DuplicatedNationalId = "user:duplicated_national_id";
        public const string DuplicatedPhoneNumber = "user:duplicated_phone_number";
    }

    /// <summary>Password strength and history violation codes.</summary>
    public static class Passwords
    {
        public const string PasswordTooShort = "auth:password_too_short";
        public const string PasswordRequiresUniqueChars = "auth:password_requires_unique_chars";
        public const string PasswordRequiresNonAlphanumeric = "auth:password_requires_non_alphanumeric";
        public const string PasswordRequiresDigit = "auth:password_requires_digit";
        public const string PasswordRequiresLower = "auth:password_requires_lower";
        public const string PasswordRequiresUpper = "auth:password_requires_upper";
        public const string RemovePasswordRequiresAlternateLogin = "auth:remove_password_requires_alternate_login";
        public const string UsedBeforePassword = "auth:used_before_password";
        public const string RequestForgetPasswordAttemptsExceeded = "user:request_forget_password_attempts_exceeded";
        public const string RequestForgetPasswordCooldown = "user:request_forget_password_cooldown";
    }

    /// <summary>Email confirmation and verification error codes.</summary>
    public static class Emails
    {
        public const string EmailAlreadyConfirmed = "auth:email_already_confirmed";
        public const string EmailAlreadyLinked = "auth:email_already_linked";
        public const string ExpiredToken = "auth:expired_token";
        public const string RequestVerifyEmailCooldown = "user:request_verify_email_cooldown";
        public const string RequestVerifyEmailAttemptsExceeded = "user:request_verify_email_attempts_exceeded";
        public const string EmailAlreadyLinkedToOtherUser = "user:email_already_linked_to_other_user";
    }

    /// <summary>Phone number linking error codes.</summary>
    public static class PhoneNumbers
    {
        public const string PhoneNumberAlreadyLinked = "user:phone_number_already_linked";
        public const string PhoneNumberAlreadyLinkedToOtherUser = "user:phone_number_already_linked_to_other_user";
    }

    /// <summary>External (OAuth/OIDC) login linking and validation error codes.</summary>
    public static class ExternalLogins
    {
        public const string LoginNotFound = "auth:login_not_found";
        public const string InvalidLoginToken = "auth:invalid_login_token";
        public const string LoginTokenValidationFailed = "auth:login_token_validation_failed";
        public const string LoginAlreadyLinked = "auth:login_already_linked";
        public const string UserRequiresAlternateLoginOrPassword = "auth:user_requires_alternate_login_or_password";
    }

    /// <summary>Lockout management error codes.</summary>
    public static class Lockouts
    {
        public const string LockoutNotEnabled = "auth:lockout_not_enabled";
        public const string AlreadyUnlocked = "user:already_unlocked";
    }
}
