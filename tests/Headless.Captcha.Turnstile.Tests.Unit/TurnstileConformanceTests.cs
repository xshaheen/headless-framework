// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the cross-provider verifier conformance contract against the Turnstile provider.</summary>
public sealed class TurnstileConformanceTests(TurnstileVerifierFixture fixture)
    : CaptchaVerifierConformanceTests<TurnstileVerifierFixture>(fixture);
