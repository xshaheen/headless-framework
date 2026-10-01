// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Jobs.SourceGenerator;
using Headless.Messaging.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests;

/// <summary>
/// Compiles one guide example on its own, against the guide's prelude, with the Messaging and Jobs source generators
/// running, and returns the diagnostics that make it wrong.
/// </summary>
/// <remarks>
/// An example may mix using directives, assembly attributes, type declarations, loose class members, and statements
/// in any order, the way a reader sees them. The compiler keeps directives and types at file level, moves loose
/// members into a class that derives from the prelude's <c>Ambient</c> class, and moves statements into an async
/// method of that class, so the prelude's ambient names (<c>builder</c>, <c>services</c>, ...) resolve and repeated
/// type names in different examples never collide. <c>#line</c> directives map every diagnostic back to the guide's
/// line.
/// </remarks>
public static partial class ExampleCompiler
{
    private const string _DefaultAssemblyName = "MyApp";

    private static readonly CSharpParseOptions _ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(
        LanguageVersion.Latest
    );

    private static readonly CSharpParseOptions _ExampleParseOptions = _ParseOptions.WithKind(SourceCodeKind.Script);

    // Every assembly the test host resolves: the framework packages the examples call and the shared frameworks.
    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(file =>
                    !string.Equals(
                        Path.GetFileName(file),
                        Path.GetFileName(typeof(ExampleCompiler).Assembly.Location),
                        StringComparison.Ordinal
                    )
                )
                .Select(file => (MetadataReference)MetadataReference.CreateFromFile(file)),
        ]
    );

    /// <summary>
    /// Compiles <paramref name="example"/> with the <paramref name="preludes"/> of its guide and returns each failure
    /// as <c>file:line: id: message</c>; an empty list means the example compiles.
    /// </summary>
    /// <remarks>
    /// A failure is a compiler error, use of an obsolete member, or any source generator warning or error. Other
    /// warnings pass: an example is a fragment, so unused locals and similar findings say nothing about its accuracy.
    /// </remarks>
    public static IReadOnlyList<string> Compile(
        DocExample example,
        IEnumerable<(string Path, string Source)> preludes,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(example);
        Argument.IsNotNull(preludes);

        // The #line directive makes every location in the example's own tree, and every node copied from it, report
        // the guide's file and line.
        var exampleTree = CSharpSyntaxTree.ParseText(
            $"#line {example.Line + 1} \"{example.Document}\"\n{example.Code}",
            _ExampleParseOptions,
            "Example.csx",
            Encoding.UTF8,
            cancellationToken
        );

        // Translation copies nodes without the tokens a parser skipped, so a syntax error must fail on its own.
        var syntaxErrors = exampleTree
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)
            .Select(_Format)
            .ToList();

        if (syntaxErrors.Count > 0)
        {
            return syntaxErrors;
        }

        var modules = _ModuleReferences(example.Code);
        var assemblyName = modules.Count > 0 ? modules[0].Namespace : _DefaultAssemblyName;

        var trees = preludes
            .Select(prelude =>
                CSharpSyntaxTree.ParseText(
                    prelude.Source,
                    _ParseOptions,
                    prelude.Path,
                    Encoding.UTF8,
                    cancellationToken
                )
            )
            .Append(
                CSharpSyntaxTree.ParseText(
                    _Translate((CompilationUnitSyntax)exampleTree.GetRoot(cancellationToken)),
                    _ParseOptions,
                    "Example.cs",
                    Encoding.UTF8,
                    cancellationToken
                )
            );

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            _References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        CSharpGeneratorDriver
            .Create(
                [
                    new MessagingIncrementalSourceGenerator().AsSourceGenerator(),
                    new JobsIncrementalSourceGenerator().AsSourceGenerator(),
                ],
                parseOptions: _ParseOptions
            )
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out var generated,
                out var generatorDiagnostics,
                cancellationToken
            );

        // A module from an assembly the example does not show (its own consumers live elsewhere) is stubbed, so the
        // example can still name it in AddModule<T>(); the module the example's own types produce is the real one.
        var stubs = modules
            .Where(module => generated.GetTypeByMetadataName(module.FullName) is null)
            .Select(module =>
                CSharpSyntaxTree.ParseText(
                    _Stub(module),
                    _ParseOptions,
                    $"{module.FullName}.Stub.cs",
                    Encoding.UTF8,
                    cancellationToken
                )
            );
        generated = generated.AddSyntaxTrees(stubs);

        return
        [
            .. generatorDiagnostics
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
                .Concat(
                    generated
                        .GetDiagnostics(cancellationToken)
                        .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error || _IsObsolete(diagnostic))
                )
                .Select(_Format),
        ];
    }

    private static bool _IsObsolete(Diagnostic diagnostic) => diagnostic.Id is "CS0612" or "CS0618";

    private static string _Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetMappedLineSpan();
        var where = span.IsValid ? $"{span.Path}:{span.StartLinePosition.Line + 1}" : "(no location)";

        return $"{where}: {diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string _Translate(CompilationUnitSyntax root)
    {
        var fileLevel = new StringBuilder();
        var members = new StringBuilder();
        var statements = new StringBuilder();

        foreach (var node in root.Externs.Cast<SyntaxNode>().Concat(root.Usings).Concat(root.AttributeLists))
        {
            _Append(fileLevel, node);
        }

        foreach (var member in root.Members)
        {
            var target = member switch
            {
                GlobalStatementSyntax => statements,
                // A script parses a top-level `var x = ...;` as a field; without modifiers it is a local variable.
                FieldDeclarationSyntax { Modifiers.Count: 0 } => statements,
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseNamespaceDeclarationSyntax => fileLevel,
                _ => members,
            };

            _Append(target, member);
        }

        return $$"""
            {{fileLevel}}
            internal sealed class Example : DocsPrelude.Ambient
            {
            {{members}}
                private async global::System.Threading.Tasks.Task RunAsync()
                {
            {{statements}}
                    await global::System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
    }

    private static void _Append(StringBuilder builder, SyntaxNode node)
    {
        var mapped = node.GetLocation().GetMappedLineSpan();

        builder
            .Append("#line ")
            .Append(mapped.StartLinePosition.Line + 1)
            .Append(" \"")
            .Append(mapped.Path)
            .AppendLine("\"")
            .AppendLine(node.ToString())
            .AppendLine("#line default");
    }

    private static List<(string Namespace, string FullName, string Kind)> _ModuleReferences(string code)
    {
        return
        [
            .. _ModuleReference
                .Matches(code)
                .Select(match => (match.Groups["ns"].Value, match.Value, match.Groups["kind"].Value))
                .Distinct(),
        ];
    }

    private static string _Stub((string Namespace, string FullName, string Kind) module)
    {
        var (contract, catalog) = string.Equals(module.Kind, "MessagingModule", StringComparison.Ordinal)
            ? ("global::Headless.Messaging.IMessagingModule", "global::Headless.Messaging.MessagingCatalogBuilder")
            : ("global::Headless.Jobs.IJobsModule", "global::Headless.Jobs.JobsCatalogBuilder");

        return $$"""
            namespace {{module.Namespace}}
            {
                public sealed class {{module.Kind}} : {{contract}}
                {
                    static void {{contract}}.Register({{catalog}} catalog) { }
                }
            }
            """;
    }

    [GeneratedRegex(
        @"\b(?<ns>[A-Za-z_][\w.]*)\.(?<kind>MessagingModule|JobsModule)\b",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _ModuleReference { get; }
}
