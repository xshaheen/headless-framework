// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal sealed class ConsumerExecutorDescriptorComparer(ILogger logger) : IEqualityComparer<ConsumerExecutorDescriptor>
{
    public bool Equals(ConsumerExecutorDescriptor? x, ConsumerExecutorDescriptor? y)
    {
        //Check whether the compared objects reference the same data.
        if (ReferenceEquals(x, y))
        {
            logger.ConsumerDuplicates(x!.MessageName, x.SubscriptionName);
            return true;
        }

        //Check whether any of the compared objects is null.
        if (x is null || y is null)
        {
            return false;
        }

        //Check whether the ConsumerExecutorDescriptor' properties are equal.
        // Lane and kind are part of the identity: a (MessageName, subscription) pair under Bus and the same pair
        // under Queue, or competing and every-instance, are independent subscriptions and must not collapse.
        var ret =
            x.MessageName.Equals(y.MessageName, StringComparison.OrdinalIgnoreCase)
            && x.Lane == y.Lane
            && x.EveryInstance == y.EveryInstance
            && (
                (y.SubscriptionName is null && x.SubscriptionName is null)
                || x.SubscriptionName?.Equals(y.SubscriptionName, StringComparison.OrdinalIgnoreCase) == true
            );

        if (
            ret
            && (
                x.ConsumerType != y.ConsumerType || !string.Equals(x.MethodName, y.MethodName, StringComparison.Ordinal)
            )
        )
        {
            logger.ConsumerDuplicates(x.MessageName, x.SubscriptionName);
        }

        return ret;
    }

    public int GetHashCode(ConsumerExecutorDescriptor? obj)
    {
        //Check whether the object is null
        if (obj is null)
        {
            return 0;
        }

        //Get hash code for the SubscriptionName field if it is not null.
        var hashGroup = obj.SubscriptionName == null ? 0 : StringComparer.Ordinal.GetHashCode(obj.SubscriptionName);

        //Get hash code for the MessageName field.
        var hashMessageName = StringComparer.Ordinal.GetHashCode(obj.MessageName);

        // Calculate the hash code with the runtime lane so Bus and Queue do not collide.
        return HashCode.Combine(hashMessageName, hashGroup, obj.Lane, obj.EveryInstance);
    }
}
