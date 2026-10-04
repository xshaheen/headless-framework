// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests;

public sealed class FluentOptionsConsumerCompilationTests : TestBase
{
    private const string _Imports = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;
        using Headless.Messaging;
        using Headless.UnitOfWork;
        """;

    // Explicit references prevent the test host's Core dependencies from hiding a runtime packaging mistake.
    private static readonly Lazy<ImmutableArray<MetadataReference>> _RuntimeReferences = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(file =>
                    string.Equals(
                        Path.GetDirectoryName(file),
                        Path.GetDirectoryName(typeof(object).Assembly.Location),
                        StringComparison.Ordinal
                    )
                )
                .Select(file => MetadataReference.CreateFromFile(file)),
            MetadataReference.CreateFromFile(typeof(IJobScheduler).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MessageOptions).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IBus).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IQueue).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IUnitOfWork).Assembly.Location),
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
        var options = receiver switch
        {
            "scheduler" => "JobOptions",
            "bus" => "PublishOptions",
            _ => "QueueOptions",
        };
        var prefix = _Arguments(receiver, verb, requestless);
        var call = $"{receiver}.{verb}{_TypeArguments(receiver, requestless)}";
        var namedPrefix = _Arguments(receiver, verb, requestless, named: true);
        var forms = new List<(string Arguments, string Parameter)>
        {
            (prefix, "cancellationToken"),
            (_Join(prefix, "ct"), "cancellationToken"),
            (_Join(prefix, $"new {options}(), ct"), "options"),
            (_Join(prefix, "options: null"), "options"),
            (_Join(prefix, $"options: new {options}(), cancellationToken: ct"), "options"),
            (_Join(prefix, "p => p.WithCorrelationId(\"order\"), ct"), "configure"),
            (_Join(prefix, "p => { p.WithCorrelationId(\"order\"); }"), "configure"),
            (_Join(prefix, "configure"), "configure"),
            (_Join(prefix, "configure, ct"), "configure"),
            (_Join(namedPrefix, "configure: p => p.WithCorrelationId(\"order\"), cancellationToken: ct"), "configure"),
            (_Join(prefix, "default(CancellationToken)"), "cancellationToken"),
            (_Join(prefix, "cancellationToken: default"), "cancellationToken"),
            (_Join(prefix, "configure: null!"), "configure"),
            (_Join(prefix, $"(Action<{options}Builder>)null!"), "configure"),
            (_Join(prefix, $"({options}?)null"), "options"),
        };
        // An untyped null or default in the first position of EnqueueAsync<TJob>(...) also converts to the
        // argument-typed overload's TArgs, so those forms are ambiguous there by design and are left out.
        if (prefix.Length > 0)
        {
            forms.Add((_Join(prefix, "null"), "options"));
            forms.Add((_Join(prefix, "null, ct"), "options"));
            forms.Add((_Join(prefix, "default, ct"), "options"));
        }

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
        invocations.Should().HaveCount(forms.Count);
        for (var index = 0; index < forms.Count; index++)
        {
            var method = _BoundMethod(model, invocations[index]);
            var callback = forms[index].Parameter is "configure";
            var holder = receiver switch
            {
                "bus" => callback ? "BusExtensions" : "IBus",
                "queue" => callback ? "QueueExtensions" : "IQueue",
                _ => callback ? "JobSchedulerExtensions" : "IJobScheduler",
            };
            _AssertMember(method, holder, _Assembly(receiver), verb, generic: receiver is "scheduler" || !requestless);
            _AssertJobTyped(method, receiver is "scheduler" && requestless);
            method.Parameters.Select(parameter => parameter.Name).Should().Contain(forms[index].Parameter);
            method
                .Parameters.Any(parameter => parameter.Name is "options")
                .Should()
                .Be(forms[index].Parameter is "options");
            method.Parameters.Any(parameter => parameter.Name is "configure").Should().Be(callback);
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
            _RuntimeSource(
                $"_ = {receiver}.{verb}{_TypeArguments(receiver, requestless)}({_Join(_Arguments(receiver, verb, requestless), "default")});",
                "JobOptions"
            )
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
        _AssertMember(method, holder, _Assembly(receiver), verb, generic: receiver is "scheduler" || !requestless);
        _AssertJobTyped(method, receiver is "scheduler" && requestless);
        method.Parameters[^1].Type.ToDisplayString().Should().Be("System.Threading.CancellationToken");
        method
            .Parameters.Should()
            .NotContain(parameter => parameter.Name == "options" || parameter.Name == "configure");
    }

    [Theory]
    [InlineData("ConfigureDefaults", "")]
    [InlineData("ConfigureJob<Request>", "")]
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
                    .GetRoot(AbortToken)
                    .DescendantNodes()
                    .OfType<ParameterSyntax>()
                    .First(parameter => parameter.Identifier.ValueText is "jobs"),
                AbortToken
            )!
            .Type;
        for (var index = 0; index < forms.Length; index++)
        {
            var method = _BoundMethod(model, invocations[index]);
            _AssertMember(
                method,
                "JobsOptionsBuilder",
                "Headless.Jobs.Core",
                verb.Split('<')[0],
                verb.Contains('<', StringComparison.Ordinal)
            );
            method.Parameters[^1].Name.Should().Be(forms[index].Parameter);
            SymbolEqualityComparer.Default.Equals(method.ReturnType, builderType).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("ConfigureDefaults", "")]
    [InlineData("ConfigureJob<Request>", "")]
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
            var info = model.GetSymbolInfo(_AssignedInvocations(compilation).Single(), AbortToken);
            info.CandidateReason.Should().Be(CandidateReason.OverloadResolutionFailure);
            info.CandidateSymbols.Cast<IMethodSymbol>()
                .Select(method => method.Parameters[^1].Name)
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
            _ = new PublishOptionsBuilder().WithHeader("source", "checkout").Build() with { DeliveryMode = DeliveryMode.Direct };
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
                    .ConfigureDefaults(p => p.WithRetries(3))
                    .ConfigureJob<Request>(p => p.WithNodeDeathPolicy(Headless.Jobs.NodeDeathPolicy.MarkFailed))
                    .Tune("orders.ship", job => job.Options(p => p.WithRetryIntervals(2, 5)))
                    .ConfigureDefaults(new JobOptions { Retries = 3 })
                    .ConfigureJob<Request>(new JobOptions { Retries = 5 })
                    .Tune("orders.ship", job => job.Options(new JobOptions { Retries = 0 }));
                """
            ),
            core: true
        );
        _Errors(compilation).Should().BeEmpty();
    }

    [Theory]
    [InlineData("DateTime")]
    [InlineData("DateTime?")]
    public void instant_entry_points_reject_datetime_consumers(string timeType)
    {
        var compilation = _Compile(_InstantConsumer(timeType));
        var errors = _Errors(compilation);
        // Nullable arguments can produce more than one conversion diagnostic for a single rejected invocation.
        errors.Select(error => error.Location.GetLineSpan().StartLinePosition.Line).Distinct().Should().HaveCount(26);
        if (string.Equals(timeType, "DateTime", StringComparison.Ordinal))
        {
            errors.Should().OnlyContain(error => error.Id == "CS0619");
            errors
                .Should()
                .OnlyContain(error =>
                    error
                        .GetMessage(CultureInfo.InvariantCulture)
                        .Contains("explicit DateTimeOffset instant", StringComparison.Ordinal)
                );
        }
    }

    [Fact]
    public void instant_entry_points_accept_explicit_offsets_and_immediate_chain_steps()
    {
        var compilation = _Compile(_InstantConsumer("DateTimeOffset"));
        _Errors(compilation).Should().BeEmpty();
        using var image = new MemoryStream();
        compilation.Emit(image, cancellationToken: AbortToken).Success.Should().BeTrue();
    }

    private static string _InstantConsumer(string timeType) =>
        $$"""
            {{_Imports}}
            public sealed record Request;
            public static class InstantConsumer
            {
                public static void Run(IJobScheduler scheduler, Request request,
                    {{timeType}} executionTime, CancellationToken ct, Action<JobOptionsBuilder> configure)
                {
                    var node = JobChain.Start(request).Root;
                    _ = JobChain.Start<CleanupJob>(options: null);
                    _ = node.Then(request, options: null);
                    _ = node.Catch<CleanupJob>();
                    _ = scheduler.ScheduleAsync(request, executionTime, ct);
                    _ = scheduler.ScheduleAsync(request, executionTime, new JobOptions(), ct);
                    _ = scheduler.ScheduleKeyedAsync(new JobKey("key"), request, executionTime, ct);
                    _ = scheduler.ScheduleKeyedAsync(new JobKey("key"), request, executionTime, new JobOptions(), ct);
                    _ = scheduler.ReplaceKeyedAsync(new JobKey("key"), 1, request, executionTime, ct);
                    _ = scheduler.ReplaceKeyedAsync(new JobKey("key"), 1, request, executionTime, new JobOptions(), ct);
                    _ = scheduler.ScheduleAsync(request, executionTime, configure, ct);
                    _ = JobChain.Start(request, executionTime);
                    _ = JobChain.Start(request, executionTime: executionTime, options: new JobOptions());
                    _ = node.Then(request, executionTime);
                    _ = node.Then(request, executionTime: executionTime, options: new JobOptions());
                    _ = node.Catch(request, executionTime);
                    _ = node.Catch(request, executionTime: executionTime, options: new JobOptions());
                    _ = scheduler.ScheduleAsync<CleanupJob>(executionTime, ct);
                    _ = scheduler.ScheduleAsync<CleanupJob>(executionTime, new JobOptions(), ct);
                    _ = scheduler.ScheduleKeyedAsync<CleanupJob>(new JobKey("key"), executionTime, ct);
                    _ = scheduler.ScheduleKeyedAsync<CleanupJob>(new JobKey("key"), executionTime, new JobOptions(), ct);
                    _ = scheduler.ReplaceKeyedAsync<CleanupJob>(new JobKey("key"), 1, executionTime, ct);
                    _ = scheduler.ReplaceKeyedAsync<CleanupJob>(new JobKey("key"), 1, executionTime, new JobOptions(), ct);
                    _ = scheduler.ScheduleAsync<CleanupJob>(executionTime, configure, ct);
                    _ = JobChain.Start<CleanupJob>(executionTime);
                    _ = JobChain.Start<CleanupJob>(executionTime: executionTime, options: new JobOptions());
                    _ = node.Then<CleanupJob>(executionTime);
                    _ = node.Then<CleanupJob>(executionTime: executionTime, options: new JobOptions());
                    _ = node.Catch<CleanupJob>(executionTime);
                    _ = node.Catch<CleanupJob>(executionTime: executionTime, options: new JobOptions());
                }
            }

            public sealed class CleanupJob : Headless.Jobs.IJob
            {
                public ValueTask ExecuteAsync(Headless.Jobs.JobContext context, CancellationToken cancellationToken) =>
                    ValueTask.CompletedTask;
            }
            """;

    /// <summary>
    /// The arguments before the options or callback. A job without arguments is named only by its type argument, so
    /// its prefix holds just the instant or delay, and is empty for <c>EnqueueAsync</c>.
    /// </summary>
    private static string _Arguments(string receiver, string verb, bool requestless, bool named = false)
    {
        var name = receiver is "scheduler" ? "request" : "contentObj";
        var arguments =
            requestless ? ""
            : named ? $"{name}: request"
            : "request";
        return verb switch
        {
            "ScheduleAsync" => _Join(arguments, named ? "executionTime: executionTime" : "executionTime"),
            "ScheduleAfterAsync" => _Join(arguments, named ? "delay: delay" : "delay"),
            _ => arguments,
        };
    }

    private static string _TypeArguments(string receiver, bool requestless) =>
        receiver is "scheduler" && requestless ? "<CleanupJob>" : "";

    private static string _Join(string first, string second) =>
        first.Length == 0 ? second
        : second.Length == 0 ? first
        : $"{first}, {second}";

    private static void _AssertJobTyped(IMethodSymbol method, bool jobTyped)
    {
        if (!jobTyped)
        {
            return;
        }

        method.TypeArguments.Should().ContainSingle().Which.Name.Should().Be("CleanupJob");
        method
            .OriginalDefinition.TypeParameters.Single()
            .ConstraintTypes.Select(type => type.ToDisplayString())
            .Should()
            .Equal("Headless.Jobs.IJob");
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
                    DateTimeOffset executionTime, TimeSpan delay,
                    CancellationToken ct, Action<{{options}}Builder> configure)
                {
                    {{statements}}
                }
            }

            public sealed class CleanupJob : Headless.Jobs.IJob
            {
                public ValueTask ExecuteAsync(Headless.Jobs.JobContext context, CancellationToken cancellationToken) =>
                    ValueTask.CompletedTask;
            }
            """;

    private static string _ConfigurationSource(string statements) =>
        $$"""
            {{_Imports}}
            using Headless.Jobs;
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
        [
            .. compilation
                .GetDiagnostics(AbortToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
        ];

    private static InvocationExpressionSyntax[] _AssignedInvocations(CSharpCompilation compilation) =>
        [
            .. compilation
                .SyntaxTrees.Single()
                .GetRoot(AbortToken)
                .DescendantNodes()
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression)
                .OfType<AssignmentExpressionSyntax>()
                .Select(assignment => assignment.Right)
                .OfType<InvocationExpressionSyntax>(),
        ];

    private static IMethodSymbol _BoundMethod(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var info = model.GetSymbolInfo(invocation, AbortToken);
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
