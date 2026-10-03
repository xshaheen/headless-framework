// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Headless.Api.Idempotency;

[JsonSerializable(typeof(IdempotencyResponseSnapshot))]
internal sealed partial class IdempotencyJsonContext : JsonSerializerContext;
