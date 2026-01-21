// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Headless.Messaging.AwsSqs;

namespace Tests;

public sealed class AmazonPolicyExtensionsTests
{
    private const string QueueArn = "arn:aws:sqs:us-east-1:123456789012:MyQueue";
    private const string TopicArn1 = "arn:aws:sns:us-east-1:123456789012:MyTopic1";
    private const string TopicArn2 = "arn:aws:sns:us-east-1:123456789012:MyTopic2";

    [Fact]
    public void should_detect_existing_permission_with_string_like()
    {
        // given
        var policy = new Policy();
        var statement = new Statement(Statement.StatementEffect.Allow);
        statement.Resources.Add(new Resource(QueueArn));
        statement.Conditions.Add(
            new Condition
            {
                Type = nameof(ConditionFactory.StringComparisonType.StringLike),
                ConditionKey = ConditionFactory.SOURCE_ARN_CONDITION_KEY,
                Values = [TopicArn1],
            }
        );
        policy.Statements.Add(statement);

        // when
        var hasPermission = policy.HasSqsPermission(TopicArn1, QueueArn);

        // then
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public void should_detect_existing_permission_with_arn_equals()
    {
        // given
        var policy = new Policy();
        var statement = new Statement(Statement.StatementEffect.Allow);
        statement.Resources.Add(new Resource(QueueArn));
        statement.Conditions.Add(
            new Condition
            {
                Type = nameof(ConditionFactory.ArnComparisonType.ArnEquals),
                ConditionKey = ConditionFactory.SOURCE_ARN_CONDITION_KEY,
                Values = [TopicArn1],
            }
        );
        policy.Statements.Add(statement);

        // when
        var hasPermission = policy.HasSqsPermission(TopicArn1, QueueArn);

        // then
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_when_no_matching_resource()
    {
        // given
        var policy = new Policy();
        var statement = new Statement(Statement.StatementEffect.Allow);
        statement.Resources.Add(new Resource("arn:aws:sqs:us-east-1:123456789012:OtherQueue"));
        statement.Conditions.Add(ConditionFactory.NewSourceArnCondition(TopicArn1));
        policy.Statements.Add(statement);

        // when
        var hasPermission = policy.HasSqsPermission(TopicArn1, QueueArn);

        // then
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_when_no_matching_topic()
    {
        // given
        var policy = new Policy();
        var statement = new Statement(Statement.StatementEffect.Allow);
        statement.Resources.Add(new Resource(QueueArn));
        statement.Conditions.Add(ConditionFactory.NewSourceArnCondition(TopicArn2));
        policy.Statements.Add(statement);

        // when
        var hasPermission = policy.HasSqsPermission(TopicArn1, QueueArn);

        // then
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public void should_add_sqs_permissions_correctly()
    {
        // given
        var policy = new Policy();
        var topicArns = new[] { TopicArn1, TopicArn2 };

        // when
        policy.AddSqsPermissions(topicArns, QueueArn);

        // then
        policy.Statements.Should().HaveCount(1);
        var statement = policy.Statements[0];
        statement.Effect.Should().Be(Statement.StatementEffect.Allow);
        statement.Actions.Should().HaveCount(1);
        statement.Actions[0].ActionName.Should().Be("sqs:SendMessage");
        statement.Resources.Should().HaveCount(1);
        statement.Resources[0].Id.Should().Be(QueueArn);
        statement.Principals.Should().HaveCount(1);
        statement.Principals[0].Id.Should().Be("*");
        statement.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void should_handle_case_insensitive_comparisons()
    {
        // given
        var policy = new Policy();
        var statement = new Statement(Statement.StatementEffect.Allow);
        statement.Resources.Add(new Resource(QueueArn));
        statement.Conditions.Add(
            new Condition
            {
                Type = "stringlike", // lowercase
                ConditionKey = ConditionFactory.SOURCE_ARN_CONDITION_KEY,
                Values = [TopicArn1],
            }
        );
        policy.Statements.Add(statement);

        // when
        var hasPermission = policy.HasSqsPermission(TopicArn1, QueueArn);

        // then
        hasPermission.Should().BeTrue();
    }
}
