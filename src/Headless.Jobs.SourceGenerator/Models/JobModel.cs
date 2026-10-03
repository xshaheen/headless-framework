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
/// <param name="FailurePolicyTypeName">
/// The fully qualified (<c>global::</c>) name of the declared failure policy, which the registration constructs through
/// a generated factory; null when the job declares none and the host default applies.
/// </param>
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
    string ContractVersion,
    string? FailurePolicyTypeName
)
{
    public bool HasArgs => ArgsTypeName is not null;
}
