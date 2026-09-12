// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Jobs;
using Headless.Jobs.Enums;

namespace Tests;

public sealed class JobFunctionDescriptorTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1 ")]
    [InlineData(" 1")]
    [InlineData("1\n2")]
    public void rejects_invalid_contract_versions(string version)
    {
        var create = () => new JobFunctionDescriptor("stable.name", null, "", JobPriority.Normal, 0, version);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void bounds_identity_without_normalizing_case_or_truncating_unicode()
    {
        var version = new string('v', JobContract.VersionMaxLength);
        var descriptor = new JobFunctionDescriptor("Stable.Name", null, "", JobPriority.Normal, 0, version);
        descriptor.FunctionName.Should().Be("Stable.Name");
        descriptor.ContractVersion.Should().Be(version);
        var oversized = () => new JobFunctionDescriptor("stable.name", null, "", JobPriority.Normal, 0, version + "x");
        oversized.Should().Throw<ArgumentException>();
        var oversizedUnicode = () =>
            new JobFunctionDescriptor(
                "stable.name",
                null,
                "",
                JobPriority.Normal,
                0,
                string.Concat(Enumerable.Repeat("😀", 51))
            );
        oversizedUnicode.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void duplicate_name_versions_fail_deterministically_in_either_registration_order()
    {
        var first = new KeyValuePair<string, JobFunctionDescriptor>(
            "same",
            new("same", null, "", JobPriority.Normal, 0, "1")
        );
        var second = new KeyValuePair<string, JobFunctionDescriptor>(
            "same",
            new("same", null, "", JobPriority.Normal, 0, "2")
        );
        var forward = () => JobFunctionRegistryBuilder.Build([], [], [first, second]);
        var reverse = () => JobFunctionRegistryBuilder.Build([], [], [second, first]);
        var forwardFailure = forward.Should().Throw<InvalidOperationException>().Which;
        reverse.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(forwardFailure.Message);
    }

    [Fact]
    public void mismatched_descriptor_keys_fail_before_registry_publication()
    {
        var descriptor = new JobFunctionDescriptor("actual", null, "", JobPriority.Normal, 0, "v2");
        var build = () =>
            JobFunctionRegistryBuilder.Build(
                [],
                [],
                [new KeyValuePair<string, JobFunctionDescriptor>("alias", descriptor)]
            );
        build.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("alias").And.Contain("actual");
    }

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
