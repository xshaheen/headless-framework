// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

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
