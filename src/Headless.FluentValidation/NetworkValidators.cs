// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using FluentValidation.Resources;

namespace FluentValidation;

/// <summary>FluentValidation extension rules for IP address string properties.</summary>
[PublicAPI]
public static class NetworkValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T>(IRuleBuilder<T, string> rule)
    {
        /// <summary>
        /// Validates that the value is a dotted-quad IPv4 address (for example <c>192.168.0.1</c>).
        /// Shorthand forms accepted by <see cref="IPAddress"/> (such as <c>"1"</c>) are rejected.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Ipv4()
        {
            return rule.Must(_IsIpv4).WithErrorDescriptor(FluentValidatorErrorDescriber.Network.InvalidIpv4());
        }

        /// <summary>
        /// Validates that the value is an IPv6 address (for example <c>2001:db8::1</c>).
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Ipv6()
        {
            return rule.Must(_IsIpv6).WithErrorDescriptor(FluentValidatorErrorDescriber.Network.InvalidIpv6());
        }

        /// <summary>
        /// Validates that the value is either an IPv4 or IPv6 address.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> IpAddress()
        {
            return rule.Must(_IsIp).WithErrorDescriptor(FluentValidatorErrorDescriber.Network.InvalidIp());
        }
    }

#nullable restore

    private static bool _IsIpv4(string? value)
    {
        // A dotted-quad always has exactly three '.' separators; the count guard rejects the shorthand
        // forms (for example "1" -> 0.0.0.1) that IPAddress.TryParse otherwise accepts.
        return value is null
            || (
                IPAddress.TryParse(value, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork
                && value.AsSpan().Count('.') == 3
            );
    }

    private static bool _IsIpv6(string? value)
    {
        return value is null
            || (IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6);
    }

    private static bool _IsIp(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        // IPv6 (incl. IPv4-mapped forms) is unambiguous; for IPv4 apply the same dotted-quad guard as Ipv4().
        return address.AddressFamily != AddressFamily.InterNetwork || value.AsSpan().Count('.') == 3;
    }
}
