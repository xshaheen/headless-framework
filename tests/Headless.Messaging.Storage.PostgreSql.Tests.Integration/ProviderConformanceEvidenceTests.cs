// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Guarantees;

namespace Tests;

// The storage conformance suites carry their guarantee IDs on overridable harness methods; this proves every one of
// them runs on this provider instead of only looking bound in the catalogue.
public sealed class ProviderConformanceEvidenceTests : TestBase
{
    [Fact]
    public void should_run_every_guarantee_this_provider_inherits() =>
        GuaranteeBindings.GetLeafErrors(typeof(ProviderConformanceEvidenceTests).Assembly).Should().BeEmpty();
}
