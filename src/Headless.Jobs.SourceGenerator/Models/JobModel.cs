// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>Everything the emitter needs about one <c>[Job]</c> class, captured as values.</summary>
/// <param name="TypeName">
/// The fully qualified (<c>global::</c>) class name, so generated code cannot be captured by the namespace it is
/// emitted into.
/// </param>
/// <param name="InvokerName">The name of the generated invoker method; unique within the assembly.</param>
/// <param name="ArgsTypeName">
/// The fully qualified <c>TArgs</c> of an <c>IJob&lt;TArgs&gt;</c> class, or <see langword="null"/> for an
/// <c>IJob</c> class.
/// </param>
/// <param name="Disposal">How the invoker releases the instance it constructs.</param>
internal sealed record JobModel(
    string TypeName,
    string InvokerName,
    string? ArgsTypeName,
    HandlerDisposal Disposal,
    string Identity,
    string? CronExpression,
    string? TimeZone,
    int Priority,
    int MaxConcurrency,
    int? OnMissedRun,
    int? MissedRunGraceSeconds,
    int? OnOverlap,
    string ContractVersion
)
{
    public bool HasArgs => ArgsTypeName is not null;
}

/// <summary>
/// The transform output for one <c>[Job]</c> class: the emission model plus what only validation needs, kept apart so
/// a location change alone never invalidates emitted source.
/// </summary>
/// <param name="Job">
/// The emission model, or <see langword="null"/> when a diagnostic already fails the build and no invoker could compile.
/// </param>
/// <param name="Identity">The declared identity, kept for duplicate detection even when <paramref name="Job"/> is null.</param>
/// <param name="ArgsTypeName">The declared argument type, kept for duplicate detection.</param>
internal sealed record JobResult(
    JobModel? Job,
    string? Identity,
    string? ArgsTypeName,
    LocationInfo? AttributeLocation,
    EquatableArray<DiagnosticInfo> Diagnostics
);
