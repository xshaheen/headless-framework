// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy.Resources;
using Headless.Primitives;

namespace Tests;

public sealed class TenancyMessageDescriberTests
{
    private static readonly (Func<ErrorDescriptor> Describe, string Code)[] _AllDescriptors =
    [
        (TenancyMessageDescriber.ResolutionFailed, TenancyErrorCodes.ResolutionFailed),
        (TenancyMessageDescriber.Unknown, TenancyErrorCodes.Unknown),
        (TenancyMessageDescriber.Disabled, TenancyErrorCodes.Disabled),
        (TenancyMessageDescriber.IdentifierMismatch, TenancyErrorCodes.IdentifierMismatch),
        (TenancyMessageDescriber.IdentifierInvalid, TenancyErrorCodes.IdentifierInvalid),
    ];

    [Fact]
    public void should_resolve_non_empty_localized_description_for_every_code_in_english_and_arabic()
    {
        // given: the UI culture, not the static Messages.Culture, which is process-wide and would hand its culture to
        // every test in the module reading these messages at the same time
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            foreach (var (describe, code) in _AllDescriptors)
            {
                // when — invariant culture resolves the neutral (English) resource
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                var english = describe();

                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
                var arabic = describe();

                // then
                english.Code.Should().Be(code);
                english.Description.Should().NotBeNullOrWhiteSpace();

                arabic.Code.Should().Be(code);
                arabic.Description.Should().NotBeNullOrWhiteSpace();

                arabic
                    .Description.Should()
                    .NotBe(english.Description, because: $"{code} should be localized, not identical across cultures");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
