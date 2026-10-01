// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

internal static class IncrementalPipelineExtensions
{
    /// <summary>Drops the declarations a transform rejected, so later steps see only real results.</summary>
    public static IncrementalValuesProvider<T> WhereNotNull<T>(this IncrementalValuesProvider<T?> source)
        where T : class => source.Where(static result => result is not null).Select(static (result, _) => result!);

    /// <summary>
    /// The compilation's assembly name, empty when it has none. Selecting the name keeps later steps cached across
    /// edits, because the name itself rarely changes while the compilation changes on every keystroke.
    /// </summary>
    public static IncrementalValueProvider<string> SelectAssemblyName(
        this IncrementalValueProvider<Compilation> source
    ) => source.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty);
}
