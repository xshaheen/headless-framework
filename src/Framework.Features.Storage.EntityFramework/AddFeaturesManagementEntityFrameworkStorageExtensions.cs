// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Features.Storage.EntityFramework;

[PublicAPI]
public static class AddFeaturesManagementEntityFrameworkStorageExtensions
{
    public static IServiceCollection AddFeaturesManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction
    )
    {
        services.AddSingleton<IFeatureValueRecordRepository, EfFeatureValueRecordRecordRepository>();
        services.AddSingleton<IFeatureDefinitionRecordRepository, EfFeatureDefinitionRecordRepository>();
        services.AddPooledDbContextFactory<FeaturesDbContext>(optionsAction);

        return services;
    }
}
