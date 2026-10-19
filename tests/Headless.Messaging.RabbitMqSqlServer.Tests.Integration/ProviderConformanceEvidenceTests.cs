// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Guarantees;

namespace Tests;

// The end-to-end suite carries its guarantee IDs on harness methods; this proves every one of them runs on this
// broker-and-store pair instead of only looking bound in the catalogue.
public sealed class ProviderConformanceEvidenceTests : TestBase
{
    [Fact]
    public void should_run_every_guarantee_this_pair_inherits() =>
        GuaranteeBindings.GetLeafErrors(typeof(ProviderConformanceEvidenceTests).Assembly).Should().BeEmpty();
}
