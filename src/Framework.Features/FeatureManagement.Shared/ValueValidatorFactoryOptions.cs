// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Volo.Abp.FeatureManagement.JsonConverters;

namespace Volo.Abp.FeatureManagement;

public class ValueValidatorFactoryOptions
{
    public HashSet<IValueValidatorFactory> ValueValidatorFactory { get; }

    public ValueValidatorFactoryOptions()
    {
        ValueValidatorFactory = new HashSet<IValueValidatorFactory>
        {
            new ValueValidatorFactory<AlwaysValidValueValidator>("NULL"),
            new ValueValidatorFactory<BooleanValueValidator>("BOOLEAN"),
            new ValueValidatorFactory<NumericValueValidator>("NUMERIC"),
            new ValueValidatorFactory<StringValueValidator>("STRING"),
        };
    }
}
