// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

internal static class DiagnosticReporting
{
    /// <summary>
    /// Registers source output that reports diagnostics mapped to current compilation syntax trees.
    /// </summary>
    /// <remarks>
    /// Resolves diagnostic locations to compilation trees so <c>#pragma</c> warning suppressions take effect.
    /// Executed independently of generated source emission to prevent unnecessary source regeneration.
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
