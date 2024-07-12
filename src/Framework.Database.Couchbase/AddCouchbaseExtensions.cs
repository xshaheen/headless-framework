using Couchbase;
using Couchbase.Transactions.Config;
using Framework.Database.Couchbase.Clusters;
using Framework.Database.Couchbase.ContextProvider;
using Framework.Database.Couchbase.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Database.Couchbase;

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
