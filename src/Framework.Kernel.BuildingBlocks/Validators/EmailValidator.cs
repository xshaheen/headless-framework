// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Kernel.BuildingBlocks.Validators;

[PublicAPI]
public static class EmailValidator
{
    /// <summary>Checks if the given e-mail is valid using the HTML5 living standard e-mail validation Regex.</summary>
    /// <param name="email">The e-mail address to check / validate</param>
    /// <param name="requireDotInDomainName">
    /// <see langword="true"/> to only validate e-mail addresses containing a dot in the domain name segment,
    /// <see langword="false"/> to allow "dot-less" domains (default: <see langword="false"/>)
    /// </param>
    /// <returns><see langword="true"/> if the e-mail address is valid, <see langword="false"/> otherwise.</returns>
    public static bool IsValid(string? email, bool requireDotInDomainName = false)
    {
        var isValid = email is not null && RegexPatterns.EmailAddress().IsMatch(email);

        if (!isValid || !requireDotInDomainName)
        {
            return isValid;
        }

        var arr = email!.Split('@', StringSplitOptions.RemoveEmptyEntries);

        return arr.Length == 2 && arr[1].Contains('.', StringComparison.Ordinal);
    }
}
