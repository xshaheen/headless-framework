// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;

namespace Headless.Messaging.AwsSqs;

public static class AmazonPolicyExtensions
{
    /// <summary>
    /// Check to see if the policy for the queue has already given permission to the topic.
    /// </summary>
    /// <param name="policy"></param>
    /// <param name="topicArn"></param>
    /// <param name="sqsQueueArn"></param>
    /// <returns></returns>
    public static bool HasSqsPermission(this Policy policy, string topicArn, string sqsQueueArn)
    {
        foreach (var statement in policy.Statements)
        {
            var containsResource = statement.Resources.Any(r => r.Id.Equals(sqsQueueArn, StringComparison.Ordinal));

            if (!containsResource)
            {
                continue;
            }

            foreach (var condition in statement.Conditions)
            {
                if (
                    (
                        string.Equals(
                            condition.Type,
                            nameof(ConditionFactory.StringComparisonType.StringLike),
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            condition.Type,
                            nameof(ConditionFactory.StringComparisonType.StringEquals),
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            condition.Type,
                            nameof(ConditionFactory.ArnComparisonType.ArnEquals),
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            condition.Type,
                            nameof(ConditionFactory.ArnComparisonType.ArnLike),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    && string.Equals(
                        condition.ConditionKey,
                        ConditionFactory.SOURCE_ARN_CONDITION_KEY,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && condition.Values.Contains(topicArn)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Add statement to the SQS policy that gives the SNS topics access to send a message to the queue.
    /// </summary>
    /// <code>
    /// {
    ///     "Version": "2012-10-17",
    ///     "Statement": [
    ///     {
    ///         "Effect": "Allow",
    ///         "Principal": {
    ///             "AWS": "*"
    ///         },
    ///         "Action": "sqs:SendMessage",
    ///         "Resource": "arn:aws:sqs:us-east-1:MyQueue",
    ///         "Condition": {
    ///             "ArnLike": {
    ///                 "aws:SourceArn": [
    ///                 "arn:aws:sns:us-east-1:FirstTopic",
    ///                 "arn:aws:sns:us-east-1:SecondTopic"
    ///                     ]
    ///             }
    ///         }
    ///     }]
    /// }
    /// </code>
    /// <param name="policy"></param>
    /// <param name="topicArns"></param>
    /// <param name="sqsQueueArn"></param>
    public static void AddSqsPermissions(this Policy policy, IEnumerable<string> topicArns, string sqsQueueArn)
    {
        var statement = new Statement(Statement.StatementEffect.Allow);
        statement.Actions.Add(new ActionIdentifier("sqs:SendMessage"));
        statement.Resources.Add(new Resource(sqsQueueArn));
        statement.Principals.Add(new Principal("*"));
        foreach (var topicArn in topicArns)
        {
            statement.Conditions.Add(ConditionFactory.NewSourceArnCondition(topicArn));
        }

        policy.Statements.Add(statement);
    }
}
