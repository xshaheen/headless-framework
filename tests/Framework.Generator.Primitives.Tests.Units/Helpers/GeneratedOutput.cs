// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests.Helpers;

public sealed record GeneratedOutput(
    ImmutableArray<Diagnostic> Diagnostics,
    List<string> Files,
    GeneratorDriver Driver
);
