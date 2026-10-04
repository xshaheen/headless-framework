// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

internal static class IncrementalPipelineExtensions
{
    /// <summary>Filters out null values from an incremental provider pipeline.</summary>
    public static IncrementalValuesProvider<T> WhereNotNull<T>(this IncrementalValuesProvider<T?> source)
        where T : class => source.Where(static result => result is not null).Select(static (result, _) => result!);

    /// <summary>
    /// Selects the compilation assembly name to preserve pipeline cache validity across edits.
    /// </summary>
    public static IncrementalValueProvider<string> SelectAssemblyName(
        this IncrementalValueProvider<Compilation> source
    ) => source.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty);
}
