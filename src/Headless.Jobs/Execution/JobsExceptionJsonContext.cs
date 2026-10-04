// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Headless.Jobs;

/// <summary>
/// Source-generated metadata for the exception detail Jobs persists with a failed job, so recording a failure needs
/// no reflection-based serialization. Default options keep the stored JSON identical to reflection output.
/// </summary>
[JsonSerializable(typeof(ExceptionDetailClassForSerialization))]
internal sealed partial class JobsExceptionJsonContext : JsonSerializerContext;
