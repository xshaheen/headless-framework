// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.NetworkInformation;
using System.Security.Cryptography;
using Framework.Checks;
using Framework.Core;

namespace Framework.Abstractions;

public interface ILongIdGenerator
{
    /// <summary>Creates a new <see cref="long"/>.</summary>
    long Create();
}

/// <summary>
/// An implementation of <see cref="ILongIdGenerator"/> that generates unique long IDs using the Snowflake algorithm.
/// You should provide a unique generatorId for each instance of your application to ensure uniqueness across distributed systems.
/// </summary>
public sealed class SnowflakeIdLongIdGenerator : ILongIdGenerator
{
    private readonly SnowflakeId _generator;

    public SnowflakeIdLongIdGenerator()
    {
        _generator = new SnowflakeId(GenerateWorkerId());
    }

    public SnowflakeIdLongIdGenerator(short generatorId)
    {
        Argument.IsNotDefault(generatorId);
        _generator = new SnowflakeId(generatorId);
    }

    public long Create() => _generator.NewId();

    /// <summary>Auto generate workerId, try using mac first, if failed, then randomly generate one</summary>
    /// <returns>workerId</returns>
    public static short GenerateWorkerId()
    {
        try
        {
            return _GenerateWorkerIdBaseOnMac();
        }
        catch
        {
#pragma warning disable ERP022 // Justification: Catching general exception to provide fallback logic.
            return (short)RandomNumberGenerator.GetInt32(0, 1024);
#pragma warning restore ERP022
        }
    }

    /// <summary>
    /// use lowest 10 bit of available MAC as workerId
    /// </summary>
    /// <returns>workerId</returns>
    private static short _GenerateWorkerIdBaseOnMac()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        // Exclude virtual and Loopback
        var firstUpInterface =
            interfaces
                .OrderByDescending(x => x.Speed)
                .FirstOrDefault(x =>
                    !x.Description.Contains("Virtual", StringComparison.Ordinal)
                    && x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && x.OperationalStatus == OperationalStatus.Up
                ) ?? throw new InvalidOperationException("no available mac found");

        var address = firstUpInterface.GetPhysicalAddress();
        var mac = address.GetAddressBytes();

        return (short)(((mac[4] & 0B11) << 8) | (mac[5] & 0xFF)); // 10 bits (0-1023)
    }
}
