// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;

namespace Headless.Messaging.Aws;

/// <summary>Extension methods for AWS IAM policy inspection and mutation used by the SQS/SNS transport.</summary>
internal static class AmazonPolicyExtensions
{
    /// <summary>
    /// Determines whether the SQS queue policy already grants the specified SNS topic permission
    /// to send messages.
    /// </summary>
    /// <param name="policy">The SQS queue access-control policy to inspect.</param>
    /// <param name="topicArn">The SNS topic ARN to check for.</param>
    /// <param name="sqsQueueArn">The SQS queue ARN that the permission must target.</param>
    /// <returns>
    /// <see langword="true"/> if a matching <c>sqs:SendMessage</c> statement already exists;
    /// <see langword="false"/> otherwise.
    /// </returns>
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
    /// Appends an <c>sqs:SendMessage</c> allow statement to the SQS queue policy, granting each
    /// of the specified SNS topics permission to deliver messages to the queue.
    /// </summary>
    /// <remarks>
    /// The generated statement uses <c>ArnLike</c> conditions on <c>aws:SourceArn</c> so that
    /// only the listed SNS topics can send to the queue. Example of the produced policy fragment:
    /// <code>
    /// {
    ///   "Effect": "Allow",
    ///   "Principal": { "AWS": "*" },
    ///   "Action": "sqs:SendMessage",
    ///   "Resource": "arn:aws:sqs:us-east-1:MyQueue",
    ///   "Condition": {
    ///     "ArnLike": { "aws:SourceArn": ["arn:aws:sns:us-east-1:FirstTopic", ...] }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    /// <param name="policy">The SQS queue policy to modify.</param>
    /// <param name="topicArns">The SNS topic ARNs to grant send access from.</param>
    /// <param name="sqsQueueArn">The SQS queue ARN that the permission applies to.</param>
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
