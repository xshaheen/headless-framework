// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using DeviceId;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

[PublicAPI]
public static class DeviceIdHelper
{
    private static readonly DeviceIdBuilder _DeviceIdBuilder = new DeviceIdBuilder()
        .AddMachineName()
        .AddMacAddress(excludeDockerBridge: true)
        .OnWindows(x => x.AddMotherboardSerialNumber())
        .OnLinux(x => x.AddMotherboardSerialNumber())
        .OnWindows(x => x.AddProcessorId())
        .OnLinux(x => x.AddCpuInfo());

    public static string GetDeviceId() => _DeviceIdBuilder.ToString();
}
