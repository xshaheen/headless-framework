// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Jobs;
using Headless.Jobs.Enums;

namespace Tests;

public sealed class JobFunctionDescriptorTests
{
    [Fact]
    public void should_preserve_generated_metadata_without_a_delegate()
    {
        var descriptor = new JobFunctionDescriptor("example", typeof(Request), "", JobPriority.High, 2);

        descriptor.FunctionName.Should().Be("example");
        descriptor.RequestType.Should().Be<Request>();
        descriptor.Priority.Should().Be(JobPriority.High);
        descriptor.MaxConcurrency.Should().Be(2);
        typeof(JobFunctionDescriptor)
            .GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Delegate", StringComparison.Ordinal));
    }

    [Fact]
    public void should_use_null_request_type_as_the_requestless_marker()
    {
        var descriptor = new JobFunctionDescriptor("cleanup", null, "", JobPriority.Normal, 0);

        descriptor.RequestType.Should().BeNull();
    }

    [Fact]
    public void should_validate_generated_metadata()
    {
        var emptyName = () => new JobFunctionDescriptor(" ", null, "", JobPriority.Normal, 0);
        var nullCron = () => new JobFunctionDescriptor("name", null, null!, JobPriority.Normal, 0);
        var invalidPriority = () => new JobFunctionDescriptor("name", null, "", (JobPriority)999, 0);
        var negativeConcurrency = () => new JobFunctionDescriptor("name", null, "", JobPriority.Normal, -1);

        emptyName.Should().Throw<ArgumentException>();
        nullCron.Should().Throw<ArgumentNullException>();
        invalidPriority.Should().Throw<InvalidEnumArgumentException>();
        negativeConcurrency.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed record Request;
}
