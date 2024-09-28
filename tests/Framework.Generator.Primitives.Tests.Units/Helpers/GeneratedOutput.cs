// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.CodeAnalysis;

namespace Tests.Helpers;

public sealed record GeneratedOutput(
    ImmutableArray<Diagnostic> Diagnostics,
    List<string> Files,
    GeneratorDriver Driver
);
