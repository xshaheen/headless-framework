// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests;

public sealed class FluentOptionsConsumerCompilationTests
{
    private const string _Imports = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;
        using Headless.Jobs.Interfaces;
        using Headless.Jobs.Models;
        using Headless.Messaging;
        """;

    // Explicit references prevent the test host's Core dependencies from hiding a runtime packaging mistake.
    private static readonly Lazy<ImmutableArray<MetadataReference>> _RuntimeReferences = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(file => Path.GetDirectoryName(file) == Path.GetDirectoryName(typeof(object).Assembly.Location))
                .Select(file => MetadataReference.CreateFromFile(file)),
            MetadataReference.CreateFromFile(typeof(IJobScheduler).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MessageOptions).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IBus).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IQueue).Assembly.Location),
        ]
    );

    [Theory]
    [InlineData("bus", "PublishAsync", false)]
    [InlineData("queue", "EnqueueAsync", false)]
    [InlineData("scheduler", "EnqueueAsync", false)]
    [InlineData("scheduler", "EnqueueAsync", true)]
    [InlineData("scheduler", "ScheduleAsync", false)]
    [InlineData("scheduler", "ScheduleAsync", true)]
    [InlineData("scheduler", "ScheduleAfterAsync", false)]
    [InlineData("scheduler", "ScheduleAfterAsync", true)]
    public void runtime_forms_bind_to_the_expected_actual_abstraction_member(
        string receiver,
        string verb,
        bool requestless
    )
    {
        var options =
            receiver == "scheduler" ? "JobOptions"
            : receiver == "bus" ? "PublishOptions"
            : "QueueOptions";
        var prefix = _Arguments(receiver, verb, requestless);
        var call = $"{receiver}.{verb}";
        var namedPrefix = _Arguments(receiver, verb, requestless, named: true);
        var forms = new (string Arguments, string Parameter)[]
        {
            (prefix, "cancellationToken"),
            ($"{prefix}, ct", "cancellationToken"),
            ($"{prefix}, new {options}(), ct", "options"),
            ($"{prefix}, options: null", "options"),
            ($"{prefix}, options: new {options}(), cancellationToken: ct", "options"),
            ($"{prefix}, p => p.WithCorrelationId(\"order\"), ct", "configure"),
            ($"{prefix}, p => {{ p.WithCorrelationId(\"order\"); }}", "configure"),
            ($"{prefix}, configure", "configure"),
            ($"{prefix}, configure, ct", "configure"),
            ($"{namedPrefix}, configure: p => p.WithCorrelationId(\"order\"), cancellationToken: ct", "configure"),
            ($"{prefix}, null", "options"),
            ($"{prefix}, null, ct", "options"),
            ($"{prefix}, default, ct", "options"),
            ($"{prefix}, default(CancellationToken)", "cancellationToken"),
            ($"{prefix}, cancellationToken: default", "cancellationToken"),
            ($"{prefix}, configure: null!", "configure"),
            ($"{prefix}, (Action<{options}Builder>)null!", "configure"),
            ($"{prefix}, ({options}?)null", "options"),
        };
        var statements = string.Join(Environment.NewLine, forms.Select(form => $"_ = {call}({form.Arguments});"));
        var compilation = _Compile(_RuntimeSource(statements, options));
        _Errors(compilation).Should().BeEmpty();
        compilation
            .ReferencedAssemblyNames.Should()
            .NotContain(assembly =>
                assembly.Name.StartsWith("Headless.", StringComparison.Ordinal)
                && assembly.Name.EndsWith(".Core", StringComparison.Ordinal)
            );

        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        var invocations = _AssignedInvocations(compilation);
        invocations.Should().HaveCount(forms.Length);
        for (var index = 0; index < forms.Length; index++)
        {
            var method = _BoundMethod(model, invocations[index]);
            var callback = forms[index].Parameter == "configure";
            var holder = receiver switch
            {
                "bus" => callback ? "BusExtensions" : "IBus",
                "queue" => callback ? "QueueExtensions" : "IQueue",
                _ => callback ? "JobSchedulerExtensions" : "IJobScheduler",
            };
            _AssertMember(method, holder, _Assembly(receiver), verb, generic: !requestless);
            method.Parameters.Select(parameter => parameter.Name).Should().Contain(forms[index].Parameter);
            method
                .Parameters.Any(parameter => parameter.Name == "options")
                .Should()
                .Be(forms[index].Parameter == "options");
            method.Parameters.Any(parameter => parameter.Name == "configure").Should().Be(callback);
        }
    }

    [Theory]
    [InlineData("bus", "PublishAsync", false)]
    [InlineData("queue", "EnqueueAsync", false)]
    [InlineData("scheduler", "EnqueueAsync", false)]
    [InlineData("scheduler", "EnqueueAsync", true)]
    [InlineData("scheduler", "ScheduleAsync", false)]
    [InlineData("scheduler", "ScheduleAsync", true)]
    [InlineData("scheduler", "ScheduleAfterAsync", false)]
    [InlineData("scheduler", "ScheduleAfterAsync", true)]
    public void positional_runtime_default_selects_the_existing_token_overload(
        string receiver,
        string verb,
        bool requestless
    )
    {
        var compilation = _Compile(
            _RuntimeSource($"_ = {receiver}.{verb}({_Arguments(receiver, verb, requestless)}, default);", "JobOptions")
        );
        _Errors(compilation).Should().BeEmpty();
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        var method = _BoundMethod(model, _AssignedInvocations(compilation).Single());
        var holder = receiver switch
        {
            "bus" => "IBus",
            "queue" => "IQueue",
            _ => "IJobScheduler",
        };
        _AssertMember(method, holder, _Assembly(receiver), verb, generic: !requestless);
        method.Parameters.Last().Type.ToDisplayString().Should().Be("System.Threading.CancellationToken");
        method
            .Parameters.Should()
            .NotContain(parameter => parameter.Name == "options" || parameter.Name == "configure");
    }

    [Theory]
    [InlineData("ConfigureDefaults", "")]
    [InlineData("ConfigureJob<Request>", "")]
    [InlineData("ConfigureJob", "descriptor, ")]
    public void configuration_forms_preserve_record_and_callback_binding_and_exact_fluent_type(
        string verb,
        string prefix
    )
    {
        var forms = new (string Argument, string Parameter)[]
        {
            ("p => p.WithRetries(3)", "configure"),
            ("p => { p.WithRetries(3); }", "configure"),
            ("configure", "configure"),
            ("configure: null!", "configure"),
            ("(Action<JobOptionsBuilder>)null!", "configure"),
            ("new JobOptions()", "options"),
            ("options: null!", "options"),
            ("(JobOptions)null!", "options"),
            ("options: default!", "options"),
            ("configure: default!", "configure"),
        };
        var statements = string.Join(
            Environment.NewLine,
            forms.Select(form => $"_ = jobs.{verb}({prefix}{form.Argument});")
        );
        var compilation = _Compile(_ConfigurationSource(statements), core: true);
        _Errors(compilation).Should().BeEmpty();
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        var invocations = _AssignedInvocations(compilation);
        invocations.Should().HaveCount(forms.Length);
        var builderType = model
            .GetDeclaredSymbol(
                compilation
                    .SyntaxTrees.Single()
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<ParameterSyntax>()
                    .First(parameter => parameter.Identifier.ValueText == "jobs")
            )!
            .Type;
        for (var index = 0; index < forms.Length; index++)
        {
            var method = _BoundMethod(model, invocations[index]);
            _AssertMember(method, "JobsOptionsBuilder", "Headless.Jobs.Core", verb.Split('<')[0], verb.Contains('<'));
            method.Parameters.Last().Name.Should().Be(forms[index].Parameter);
            SymbolEqualityComparer.Default.Equals(method.ReturnType, builderType).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("ConfigureDefaults", "")]
    [InlineData("ConfigureJob<Request>", "")]
    [InlineData("ConfigureJob", "descriptor, ")]
    public void untyped_configuration_null_and_default_are_ambiguous_between_record_and_callback(
        string verb,
        string prefix
    )
    {
        foreach (var argument in new[] { "null", "default" })
        {
            var compilation = _Compile(_ConfigurationSource($"_ = jobs.{verb}({prefix}{argument});"), core: true);
            _Errors(compilation).Select(diagnostic => diagnostic.Id).Should().Equal("CS0121");
            var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
            var info = model.GetSymbolInfo(_AssignedInvocations(compilation).Single());
            info.CandidateReason.Should().Be(CandidateReason.OverloadResolutionFailure);
            info.CandidateSymbols.Cast<IMethodSymbol>()
                .Select(method => method.Parameters.Last().Name)
                .Distinct(StringComparer.Ordinal)
                .Should()
                .BeEquivalentTo("options", "configure");
            info.CandidateSymbols.Should()
                .OnlyContain(symbol => symbol.ContainingAssembly.Name == "Headless.Jobs.Core");
        }
    }

    [Fact]
    public void builders_coexist_and_explicit_generic_null_payloads_select_the_fluent_helpers()
    {
        var source = _RuntimeSource(
            """
            _ = new JobOptionsBuilder().WithRetries(0).Build();
            _ = new PublishOptionsBuilder().WithHeader("source", "checkout").Build() with { DeliveryMode = DeliveryMode.Auto };
            _ = new QueueOptionsBuilder().WithDelay(TimeSpan.FromSeconds(1)).Build();
            _ = bus.PublishAsync<Request>(null, p => p.WithCorrelationId("order"), ct);
            _ = queue.EnqueueAsync<Request>(null, p => p.WithCorrelationId("order"), ct);
            _ = scheduler.EnqueueAsync<Request?>(null, p => p.WithRetries(0), ct);
            _ = scheduler.ScheduleAsync<Request?>(null, executionTime, p => p.WithRetries(0), ct);
            _ = scheduler.ScheduleAfterAsync<Request?>(null, delay, p => p.WithRetries(0), ct);
            """,
            "JobOptions"
        );
        var compilation = _Compile(source);
        _Errors(compilation).Should().BeEmpty();
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        var calls = _AssignedInvocations(compilation)
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax });
        calls.Should().HaveCount(5);
        foreach (var invocation in calls)
        {
            var method = _BoundMethod(model, invocation);
            method.IsGenericMethod.Should().BeTrue();
            method.Parameters.Should().Contain(parameter => parameter.Name == "configure");
        }
    }

    [Fact]
    public void configuration_callbacks_chain_with_records_on_the_exact_generic_builder()
    {
        var compilation = _Compile(
            _ConfigurationSource(
                """
                JobsOptionsBuilder<TimeJobEntity, CronJobEntity> result = jobs
                    .ConfigureDefaults(p => p.WithRetries(3).RequireAtomicEnlistment())
                    .ConfigureJob<Request>(p => p.WithNodeDeathPolicy(Headless.Jobs.Enums.NodeDeathPolicy.MarkFailed))
                    .ConfigureJob(descriptor, p => p.WithRetryIntervals(2, 5))
                    .ConfigureDefaults(new JobOptions { Retries = 3 })
                    .ConfigureJob<Request>(new JobOptions { Retries = 5 })
                    .ConfigureJob(descriptor, new JobOptions { Retries = 0 });
                """
            ),
            core: true
        );
        _Errors(compilation).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Jobs", "class JobCaller")]
    [InlineData("Bus", "class OrderEvents")]
    [InlineData("Queue", "class ImportJobs")]
    public void actual_readme_examples_compile_with_documented_imports_and_only_abstraction_references(
        string resource,
        string marker
    )
    {
        using var stream = typeof(FluentOptionsConsumerCompilationTests).Assembly.GetManifestResourceStream(
            $"FluentOptions.{resource}"
        );
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!);
        var markdown = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var example = markdown
            .Split("```csharp\n", StringSplitOptions.None)
            .Skip(1)
            .Select(block => block.Split("```", StringSplitOptions.None)[0])
            .Single(block => block.Contains(marker, StringComparison.Ordinal));
        // These are the SDK implicit System imports; all framework imports come from the README itself.
        var compilation = _Compile("using System; using System.Threading; using System.Threading.Tasks;\n" + example);
        _Errors(compilation).Should().BeEmpty();
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static string _Arguments(string receiver, string verb, bool requestless, bool named = false)
    {
        var payload = requestless ? "descriptor" : "request";
        var name = receiver == "scheduler" ? payload : "contentObj";
        var arguments = named ? $"{name}: {payload}" : payload;
        return verb switch
        {
            "ScheduleAsync" => arguments + (named ? ", executionTime: executionTime" : ", executionTime"),
            "ScheduleAfterAsync" => arguments + (named ? ", delay: delay" : ", delay"),
            _ => arguments,
        };
    }

    private static string _Assembly(string receiver) =>
        receiver switch
        {
            "bus" => "Headless.Messaging.Bus.Abstractions",
            "queue" => "Headless.Messaging.Queue.Abstractions",
            _ => "Headless.Jobs.Abstractions",
        };

    private static string _RuntimeSource(string statements, string options) =>
        $$"""
            {{_Imports}}
            public sealed record Request;
            public static class Consumer
            {
                public static void Run(IBus bus, IQueue queue, IJobScheduler scheduler, Request request,
                    JobFunctionDescriptor descriptor, DateTimeOffset executionTime, TimeSpan delay,
                    CancellationToken ct, Action<{{options}}Builder> configure)
                {
                    {{statements}}
                }
            }
            """;

    private static string _ConfigurationSource(string statements) =>
        $$"""
            {{_Imports}}
            using Headless.Jobs.Entities;
            public sealed record Request;
            public static class Consumer
            {
                public static void Run(JobsOptionsBuilder<TimeJobEntity, CronJobEntity> jobs,
                    JobFunctionDescriptor descriptor, Action<JobOptionsBuilder> configure)
                {
                    {{statements}}
                }
            }
            """;

    private static CSharpCompilation _Compile(string source, bool core = false)
    {
        var references = _RuntimeReferences.Value;
        if (core)
        {
            references = references.Add(
                MetadataReference.CreateFromFile(
                    typeof(JobsOptionsBuilder<TimeJobEntity, CronJobEntity>).Assembly.Location
                )
            );
        }

        return CSharpCompilation.Create(
            "FluentOptions.Consumer",
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14)
                ),
            ],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static Diagnostic[] _Errors(CSharpCompilation compilation) =>
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

    private static InvocationExpressionSyntax[] _AssignedInvocations(CSharpCompilation compilation) =>
        compilation
            .SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Select(statement => statement.Expression)
            .OfType<AssignmentExpressionSyntax>()
            .Select(assignment => assignment.Right)
            .OfType<InvocationExpressionSyntax>()
            .ToArray();

    private static IMethodSymbol _BoundMethod(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var info = model.GetSymbolInfo(invocation);
        info.CandidateReason.Should().Be(CandidateReason.None, invocation.ToString());
        return info.Symbol.Should().BeAssignableTo<IMethodSymbol>().Which;
    }

    private static void _AssertMember(IMethodSymbol method, string holder, string assembly, string name, bool generic)
    {
        var owner = method.ContainingType;
        while (owner.ContainingType is { } enclosing)
        {
            owner = enclosing;
        }

        owner.Name.Should().Be(holder);
        method.ContainingAssembly.Name.Should().Be(assembly);
        method.Name.Should().Be(name);
        method.IsGenericMethod.Should().Be(generic);
    }
}
