// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Volo.Abp.FeatureManagement.JsonConverters;

public interface IValueValidatorFactory
{
    bool CanCreate(string name);

    IValueValidator Create();
}
