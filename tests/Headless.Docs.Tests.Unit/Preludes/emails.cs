// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the AWS options, configuration sections, and payload variables the email guide's examples assume.

global using static EmailsAmbient;
using Amazon.Extensions.NETCore.Setup;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class EmailsAmbient
{
    public static AWSOptions awsOptions => null!;

    public static IConfigurationSection smtpSection => null!;

    public static IConfigurationSection acsSection => null!;

    public static string body => null!;
}
