using Tests.TestSetup;

namespace Tests;

[Collection(nameof(TusAzureFixture))]
public sealed class TusAzureStoreTests(TusAzureFixture fixture, ITestOutputHelper output) { }
