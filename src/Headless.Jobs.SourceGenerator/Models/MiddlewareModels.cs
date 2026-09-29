// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>Where a Jobs middleware attribute was applied.</summary>
internal enum MiddlewarePlacement
{
    Assembly,
    Method,
}

/// <summary>
/// One <c>JobScheduleMiddleware&lt;T&gt;</c> or <c>JobExecuteMiddleware&lt;T&gt;</c> declaration, captured as values.
/// Target resolution and duplicate detection need the whole assembly, so they happen after collection.
/// </summary>
/// <param name="Function">
/// The target function: the <c>Function</c> property for assembly placement, or the neighboring
/// <c>[JobFunction]</c> name for method placement.
/// </param>
/// <param name="TypeIdentity">The middleware's metadata identity, prefixed with the assembly name when registered.</param>
/// <param name="ImplementsInterface">
/// <see langword="false"/> when the type does not implement the stage interface; the generic constraint already
/// reports that, so the declaration is dropped without a second diagnostic.
/// </param>
internal sealed record MiddlewareDeclarationModel(
    MiddlewarePlacement Placement,
    bool IsSchedule,
    string? Function,
    int Priority,
    string TypeName,
    string TypeDisplayName,
    string TypeIdentity,
    bool IsAccessible,
    bool ImplementsInterface,
    LocationInfo? Location
);

/// <summary>
/// Transform output for one attributed declaration: the middleware it declares plus diagnostics that need no
/// cross-assembly context (for example, method placement without a <c>[JobFunction]</c>).
/// </summary>
internal sealed record MiddlewareResult(
    EquatableArray<MiddlewareDeclarationModel> Declarations,
    EquatableArray<DiagnosticInfo> Diagnostics
);

/// <summary>A resolved, de-duplicated middleware registration.</summary>
internal sealed record MiddlewareRegistrationModel(
    string TypeName,
    string Identity,
    string? Function,
    int Priority,
    bool IsSchedule
);
