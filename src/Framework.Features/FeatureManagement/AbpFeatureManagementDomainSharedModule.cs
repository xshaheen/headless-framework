// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.FeatureManagement.JsonConverters;
using Framework.Features.FeatureManagement.Localization;

namespace Framework.Features.FeatureManagement;

[DependsOn(typeof(AbpValidationModule), typeof(AbpJsonSystemTextJsonModule))]
public class AbpFeatureManagementDomainSharedModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpFeatureManagementDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options
                .Resources.Add<AbpFeatureManagementResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("Volo/Abp/FeatureManagement/Localization/Domain");
        });

        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("Volo.Abp.FeatureManagement", typeof(AbpFeatureManagementResource));
        });

        var valueValidatorFactoryOptions = context.Services.GetPreConfigureActions<ValueValidatorFactoryOptions>();
        Configure<ValueValidatorFactoryOptions>(options =>
        {
            valueValidatorFactoryOptions.Configure(options);
        });

        Configure<AbpSystemTextJsonSerializerOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(
                new StringValueTypeJsonConverter(valueValidatorFactoryOptions.Configure())
            );
        });
    }
}
