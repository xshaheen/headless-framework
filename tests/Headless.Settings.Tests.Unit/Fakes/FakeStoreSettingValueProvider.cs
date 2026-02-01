// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.ValueProviders;
using Headless.Settings.Values;

namespace Tests.Fakes;

public sealed class FakeStoreSettingValueProvider(ISettingValueStore store) : StoreSettingValueProvider(store)
{
    public override string Name => "Store";
}
