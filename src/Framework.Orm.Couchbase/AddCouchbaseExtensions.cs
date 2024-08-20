using Couchbase;
using Couchbase.Transactions.Config;
using Framework.Orm.Couchbase.Clusters;
using Framework.Orm.Couchbase.ContextProvider;
using Framework.Orm.Couchbase.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.Couchbase;

public static class AddCouchbaseExtensions
{
    public static void AddCouchbaseBucketContexts(
        this IServiceCollection services,
        ICouchbaseClusterOptionsProvider clusterOptionsProvider,
        ICouchbaseTransactionConfigProvider transactionConfigProvider
    )
    {
        services.AddSingleton(clusterOptionsProvider);
        services.AddSingleton(transactionConfigProvider);
        services.AddSingleton<ICouchbaseClustersProvider, CouchbaseClustersProvider>();
        services.AddSingleton<IBucketContextProvider, BucketContextProvider>();

        services.AddSingleton<ICouchbaseCollectionsAssemblyReader, CouchbaseCollectionsAssemblyReader>();
        services.AddTransient<ICouchbaseManager, CouchbaseManager>();
    }

    public static void AddCouchbaseBucketContexts(
        this IServiceCollection services,
        ClusterOptions defaultClusterOptions,
        Action<TransactionConfigBuilder>? defaultConfigureTransaction = null
    )
    {
        var clusterOptionsProvider = new CouchbaseClusterOptionsProvider(defaultClusterOptions);
        var transactionConfigProvider = new CouchbaseTransactionConfigProvider(defaultConfigureTransaction);

        services.AddSingleton<ICouchbaseClusterOptionsProvider>(clusterOptionsProvider);
        services.AddSingleton<ICouchbaseTransactionConfigProvider>(transactionConfigProvider);
        services.AddSingleton<ICouchbaseClustersProvider, CouchbaseClustersProvider>();
        services.AddSingleton<IBucketContextProvider, BucketContextProvider>();

        services.AddSingleton<ICouchbaseCollectionsAssemblyReader, CouchbaseCollectionsAssemblyReader>();
        services.AddTransient<ICouchbaseManager, CouchbaseManager>();
    }
}
