// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.Idempotency;
using Headless.Testing.Tests;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class IdempotentOperationsJsonExtensionsTests : TestBase
{
    [Fact]
    public async Task should_round_trip_a_typed_result_through_source_generated_metadata()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        var admission = Admitted();
        var receipt = new Receipt("R-42", 19.99m);
        ReadOnlyMemory<byte> written = default;
        operations
            .CompleteAsync(
                admission,
                Arg.Do<ReadOnlyMemory<byte>>(bytes => written = bytes.ToArray()),
                "receipt/v1",
                null,
                AbortToken
            )
            .Returns(ValueTask.CompletedTask);

        // when
        await operations.CompleteAsync(
            admission,
            receipt,
            ReceiptJsonContext.Default.Receipt,
            "receipt/v1",
            cancellationToken: AbortToken
        );
        var replayed = new IdempotentResult(written.Span, "receipt/v1").Deserialize(ReceiptJsonContext.Default.Receipt);

        // then
        replayed.Should().Be(receipt);
    }
}

public sealed record Receipt(string Number, decimal Amount);

[JsonSerializable(typeof(Receipt))]
internal sealed partial class ReceiptJsonContext : JsonSerializerContext;
