// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.SourceGenerators;

namespace Headless.Generator.Primitives.Models;

/// <summary>
/// What parsing one primitive declaration produced: the data to emit, when the declaration can be generated,
/// and the diagnostics
/// found while reading it. Diagnostics travel beside the model so reporting them never forces source to be re-emitted.
/// </summary>
internal sealed record PrimitiveParseResult(PrimitiveTypeInfo? Info, EquatableArray<DiagnosticInfo> Diagnostics);
