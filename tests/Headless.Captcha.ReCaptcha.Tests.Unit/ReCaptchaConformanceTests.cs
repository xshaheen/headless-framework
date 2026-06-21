// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the cross-provider verifier conformance contract against reCAPTCHA v2.</summary>
public sealed class ReCaptchaV2ConformanceTests(ReCaptchaV2VerifierFixture fixture)
    : CaptchaVerifierConformanceTests<ReCaptchaV2VerifierFixture>(fixture);

/// <summary>Runs the cross-provider verifier conformance contract against reCAPTCHA v3.</summary>
public sealed class ReCaptchaV3ConformanceTests(ReCaptchaV3VerifierFixture fixture)
    : CaptchaVerifierConformanceTests<ReCaptchaV3VerifierFixture>(fixture);
