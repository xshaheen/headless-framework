// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

public sealed class AzureServiceBusCredentialGateTests : TestBase
{
    [Fact]
    public void should_report_local_credential_gate_when_namespace_is_absent()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            AzureServiceBusFixture.ConnectionStringEnvironmentVariable
        );

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip(
                "A real Azure Service Bus namespace credential is present; the conformance cases will execute."
            );
        }

        connectionString.Should().BeNullOrWhiteSpace();
    }
}
