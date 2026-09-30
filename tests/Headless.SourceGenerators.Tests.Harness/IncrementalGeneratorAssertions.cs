// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Assertions that prove a generator is incremental, for a run made with
/// <see cref="GeneratorCompilation.CreateTrackingDriver"/>.
/// </summary>
public static class IncrementalGeneratorAssertions
{
    /// <summary>The run reason of every output of <paramref name="step"/>; fails when the step is not tracked.</summary>
    public static IncrementalStepRunReason[] StepReasons(GeneratorRunResult result, string step)
    {
        result.TrackedSteps.Should().ContainKey(step);
        return [.. result.TrackedSteps[step].SelectMany(run => run.Outputs).Select(output => output.Reason)];
    }

    /// <summary>
    /// Asserts every output of every step was cached or recomputed to an equal value, which is what an edit that does
    /// not change a generator's input must produce.
    /// </summary>
    public static void AssertStepsReused(GeneratorRunResult result, IEnumerable<string> steps)
    {
        foreach (var step in steps)
        {
            StepReasons(result, step)
                .Should()
                .NotBeEmpty($"step '{step}' must run")
                .And.OnlyContain(
                    reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                    $"step '{step}' must be reused after an edit that does not change its input"
                );
        }
    }

    /// <summary>
    /// Asserts no output of the given steps holds a symbol, syntax node, tree, semantic model, compilation, or location.
    /// Any of those pins a compilation in memory and compares by identity, so a step holding one is never reused.
    /// </summary>
    public static void AssertNoSymbolsOrSyntax(GeneratorRunResult result, IEnumerable<string> steps)
    {
        foreach (var step in steps)
        {
            foreach (var (value, _) in result.TrackedSteps[step].SelectMany(run => run.Outputs))
            {
                _Visit(value, step, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
        }
    }

    private static void _Visit(object? value, string path, HashSet<object> visited)
    {
        if (value is null || value is string || value is DiagnosticDescriptor || value.GetType().IsPrimitive)
        {
            return;
        }

        value
            .Should()
            .NotBeAssignableTo<ISymbol>(path)
            .And.NotBeAssignableTo<SyntaxNode>(path)
            .And.NotBeAssignableTo<SyntaxTree>(path)
            .And.NotBeAssignableTo<SemanticModel>(path)
            .And.NotBeAssignableTo<Compilation>(path)
            .And.NotBeAssignableTo<Location>(path);

        var type = value.GetType();
        if (type.IsEnum || (!type.IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is IEnumerable items)
        {
            var index = 0;
            foreach (var item in items)
            {
                _Visit(item, $"{path}[{index++}]", visited);
            }

            return;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            _Visit(field.GetValue(value), $"{path}.{field.Name}", visited);
        }
    }
}
