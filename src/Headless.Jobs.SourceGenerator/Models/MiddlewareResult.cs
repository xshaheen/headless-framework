// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>
/// Transform output for one attributed declaration: the middleware it declares plus diagnostics that need no
/// cross-assembly context (for example, class placement without <c>[Job]</c>).
/// </summary>
internal sealed record MiddlewareResult(
    EquatableArray<MiddlewareDeclarationModel> Declarations,
    EquatableArray<DiagnosticInfo> Diagnostics
);
