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
            logger.ConsumerDuplicates(x!.MessageName, x.GroupName);
            return true;
        }

        //Check whether any of the compared objects is null.
        if (x is null || y is null)
        {
            return false;
        }

        //Check whether the ConsumerExecutorDescriptor' properties are equal.
        // Lane is part of the identity: a (MessageName, Group) pair under Bus and the same pair
        // under Queue are two independent subscriptions and must not collapse.
        var ret =
            x.MessageName.Equals(y.MessageName, StringComparison.OrdinalIgnoreCase)
            && x.Lane == y.Lane
            && (
                (y.GroupName is null && x.GroupName is null)
                || x.GroupName?.Equals(y.GroupName, StringComparison.OrdinalIgnoreCase) == true
            );

        if (ret && (x.ImplTypeInfo != y.ImplTypeInfo || x.MethodInfo != y.MethodInfo))
        {
            logger.ConsumerDuplicates(x.MessageName, x.GroupName);
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

        //Get hash code for the GroupName field if it is not null.
        var hashGroup = obj.GroupName == null ? 0 : StringComparer.Ordinal.GetHashCode(obj.GroupName);

        //Get hash code for the MessageName field.
        var hashMessageName = StringComparer.Ordinal.GetHashCode(obj.MessageName);

        // Calculate the hash code with the runtime lane so Bus and Queue do not collide.
        return HashCode.Combine(hashMessageName, hashGroup, obj.Lane);
    }
}
