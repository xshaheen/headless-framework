// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Messaging stores fingerprints from the shared <see cref="FailureFingerprint"/> algorithm, and its fixed fingerprints
/// are constants that must equal what that algorithm produces for each cause, so Messaging groups stay comparable with
/// every other feature's.
/// </summary>
public sealed class MessagingFailureFingerprintsTests : TestBase
{
    [Theory]
    [InlineData(MessagingFailureFingerprints.Expired, "expired")]
    [InlineData(MessagingFailureFingerprints.Deserialization, "deserialization")]
    [InlineData(MessagingFailureFingerprints.Poison, "poison")]
    public void should_equal_the_shared_fixed_fingerprint_when_the_constant_names_a_cause(string constant, string cause)
    {
        constant.Should().Be(FailureFingerprint.ForCause(cause));
        constant.Length.Should().BeLessThanOrEqualTo(MessagingFailureFingerprints.MaxLength);
    }
}
