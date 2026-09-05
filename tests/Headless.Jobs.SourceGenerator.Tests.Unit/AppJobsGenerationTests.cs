// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests;

public sealed class AppJobsGenerationTests
{
    [Fact]
    public void requestless_handles_compile_for_punctuation_keywords_reserved_names_and_escape_lookalikes()
    {
        const string source = """
            using Headless.Jobs.Base;
            namespace Demo;
            public sealed class Handlers
            {
                [JobFunction("Cleanup")] public void Cleanup() { }
                [JobFunction("class")] public void Keyword() { }
                [JobFunction("invoice.send")] public void Punctuation() { }
                [JobFunction("invoice_u002E_send")] public void EscapeLookalike() { }
                [JobFunction("AppJobs")] public void SameName() { }
                [JobFunction("ToString")] public void ObjectName() { }
                [JobFunction("1start")] public void Digit() { }
            }
            public static class Caller
            {
                public static object[] Handles => [
                    Jobs.SourceGenerator.Tests.AppJobs.Cleanup,
                    Jobs.SourceGenerator.Tests.AppJobs.@class,
                    Jobs.SourceGenerator.Tests.AppJobs.invoice_u002E_send,
                    Jobs.SourceGenerator.Tests.AppJobs.invoice_u005F_u002E_u005F_send,
                    Jobs.SourceGenerator.Tests.AppJobs._u0041_ppJobs,
                    Jobs.SourceGenerator.Tests.AppJobs._u0054_oString,
                    Jobs.SourceGenerator.Tests.AppJobs._u0031_start
                ];
            }
            """;
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);
        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        generated.Should().Contain("""descriptors.Add("Cleanup", AppJobs.Cleanup)""");
        generated.Should().Contain("public static class AppJobs");
        generated.Should().Contain("public static JobFunctionDescriptor Cleanup { get; }");
    }
}
