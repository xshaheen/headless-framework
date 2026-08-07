// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Couchbase.Management.Eventing;
using Headless.Couchbase.Clusters;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CouchbaseEventingFunctionsSeederTests : TestBase
{
    [Fact]
    public async Task should_upsert_deployed_function_with_source_metadata_and_read_only_aliases()
    {
        var cluster = Substitute.For<ICluster>();
        var manager = Substitute.For<IEventingFunctionManager>();
        cluster.EventingFunctions.Returns(manager);
        EventingFunction? capturedFunction = null;
        UpsertFunctionOptions? capturedOptions = null;
        manager
            .UpsertFunctionAsync(Arg.Any<EventingFunction>(), Arg.Any<UpsertFunctionOptions>())
            .Returns(call =>
            {
                capturedFunction = call.ArgAt<EventingFunction>(0);
                capturedOptions = call.ArgAt<UpsertFunctionOptions>(1);
                return Task.CompletedTask;
            });
        var source = new CouchbaseKeyspace("data", "sales", "orders");
        var metadata = new CouchbaseKeyspace("system", "eventing", "metadata");
        var aliases = new Dictionary<string, CouchbaseKeyspace>(StringComparer.Ordinal)
        {
            ["products"] = new("catalog", "inventory", "products"),
        };

        await cluster.UpsertFunctionAsync(
            source,
            metadata,
            aliases,
            "project-orders",
            "function OnUpdate(doc, meta) {}",
            workers: 3,
            AbortToken
        );

        capturedFunction.Should().NotBeNull();
        capturedFunction!.Name.Should().Be("project-orders");
        capturedFunction.Code.Should().Be("function OnUpdate(doc, meta) {}");
        capturedFunction.SourceKeySpace.Should().NotBeNull();
        _ReadKeyspace(capturedFunction.SourceKeySpace!).Should().Be(source);
        _ReadKeyspace(capturedFunction.MetaDataKeySpace!).Should().Be(metadata);
        capturedFunction.Settings.DeploymentStatus.Should().Be(EventingFunctionDeploymentStatus.Deployed);
        capturedFunction.Settings.ProcessingStatus.Should().Be(EventingFunctionProcessingStatus.Running);
        capturedFunction.Settings.WorkerCount.Should().Be(3);
        var deployment = _ReadProperty<DeploymentConfig>(capturedFunction, "DeploymentConfig");
        var binding = deployment.BucketBindings.Should().ContainSingle().Which;
        binding.Alias.Should().Be("products");
        binding.Access.Should().Be(EventingFunctionBucketAccess.ReadOnly);
        _ReadBinding(binding).Should().Be(aliases["products"]);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Token.Should().Be(AbortToken);
        capturedOptions.Timeout.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task should_propagate_eventing_upsert_failure()
    {
        var cluster = Substitute.For<ICluster>();
        var manager = Substitute.For<IEventingFunctionManager>();
        cluster.EventingFunctions.Returns(manager);
        manager
            .UpsertFunctionAsync(Arg.Any<EventingFunction>(), Arg.Any<UpsertFunctionOptions>())
            .Returns(Task.FromException(new InvalidOperationException("eventing unavailable")));

        var act = () =>
            cluster.UpsertFunctionAsync(
                new CouchbaseKeyspace("data", "scope", "source"),
                new CouchbaseKeyspace("system", "scope", "metadata"),
                new Dictionary<string, CouchbaseKeyspace>(StringComparer.Ordinal),
                "function",
                "code",
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("eventing unavailable");
    }

    private static CouchbaseKeyspace _ReadKeyspace(EventingFunctionKeyspace keyspace)
    {
        return new(
            _ReadProperty<string>(keyspace, "Bucket"),
            _ReadProperty<string>(keyspace, "Scope"),
            _ReadProperty<string>(keyspace, "Collection")
        );
    }

    private static CouchbaseKeyspace _ReadBinding(EventingFunctionBucketBinding binding)
    {
        return new(
            _ReadProperty<string>(binding, "BucketName"),
            _ReadProperty<string>(binding, "ScopeName"),
            _ReadProperty<string>(binding, "CollectionName")
        );
    }

    private static T _ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance
            .GetType()
            .GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            );
        property.Should().NotBeNull();

        return property!.GetValue(instance).Should().BeOfType<T>().Subject;
    }
}
