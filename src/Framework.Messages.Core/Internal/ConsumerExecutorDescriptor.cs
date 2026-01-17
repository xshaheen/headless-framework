// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;
using Microsoft.Extensions.Logging;

namespace Framework.Messages.Internal;

public class ConsumerExecutorDescriptorComparer(ILogger logger) : IEqualityComparer<ConsumerExecutorDescriptor>
{
    public bool Equals(ConsumerExecutorDescriptor? x, ConsumerExecutorDescriptor? y)
    {
        //Check whether the compared objects reference the same data.
        if (ReferenceEquals(x, y))
        {
            logger.ConsumerDuplicates(x!.TopicName, x.Attribute.Group);
            return true;
        }

        //Check whether any of the compared objects is null.
        if (x is null || y is null)
        {
            return false;
        }

        //Check whether the ConsumerExecutorDescriptor' properties are equal.
        var ret =
            x.TopicName.Equals(y.TopicName, StringComparison.OrdinalIgnoreCase)
            && (
                (y.Attribute.Group is null && x.Attribute.Group is null)
                || x.Attribute.Group?.Equals(y.Attribute.Group, StringComparison.OrdinalIgnoreCase) == true
            );

        if (ret && (x.ImplTypeInfo != y.ImplTypeInfo || x.MethodInfo != y.MethodInfo))
        {
            logger.ConsumerDuplicates(x.TopicName, x.Attribute.Group);
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

        //Get hash code for the Attribute Group field if it is not null.
        var hashAttributeGroup = obj.Attribute?.Group == null ? 0 : obj.Attribute.Group.GetHashCode();

        //Get hash code for the TopicName field.
        var hashTopicName = obj.TopicName.GetHashCode();

        //Calculate the hash code.
        return hashAttributeGroup ^ hashTopicName;
    }
}
