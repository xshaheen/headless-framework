// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Provider correctness capabilities used by conformance and consumers.</summary>
[PublicAPI]
public sealed record ProviderCapabilities(bool FailoverEligible);
