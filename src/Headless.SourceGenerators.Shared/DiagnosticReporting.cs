// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

internal static class DiagnosticReporting
{
    /// <summary>
    /// Reports pipeline-computed diagnostics against the current compilation's syntax trees.
    /// </summary>
    /// <remarks>
    /// This output combines with the compilation so each location resolves to a real tree, which is what lets
    /// <c>#pragma</c> suppress a generator warning. It therefore re-executes on every run; that is cheap because the
    /// diagnostics themselves come from cached steps, and it is kept separate from source output so re-reporting never
    /// forces source to be re-emitted.
    /// </remarks>
    public static void RegisterDiagnosticsOutput(
        this IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<DiagnosticInfo>> diagnostics
    )
    {
        context.RegisterSourceOutput(
            diagnostics.Combine(context.CompilationProvider),
            static (productionContext, source) =>
            {
                var (items, compilation) = source;
                if (items.Count == 0)
                {
                    return;
                }

                var trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (!trees.ContainsKey(tree.FilePath))
                    {
                        trees.Add(tree.FilePath, tree);
                    }
                }

                foreach (var item in items)
                {
                    productionContext.ReportDiagnostic(
                        item.ToDiagnostic(path => trees.TryGetValue(path, out var tree) ? tree : null)
                    );
                }
            }
        );
    }
}
