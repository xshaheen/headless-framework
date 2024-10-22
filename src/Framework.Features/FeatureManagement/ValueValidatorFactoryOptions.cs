// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.FeatureManagement.JsonConverters;

namespace Framework.Features.FeatureManagement;

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
