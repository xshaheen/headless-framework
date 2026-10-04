// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;

namespace Tests.Fakes;

public sealed class FakeStoreSettingValueProvider(ISettingValueStore store) : StoreSettingValueProvider(store)
{
    public override string Name => "Store";
}
