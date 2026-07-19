// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Text;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlCancellationProcessSmokeTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    private const string _ConnectionEnvironmentVariable = "HEADLESS_JOBS_CANCELLATION_SMOKE_CONNECTION";

    [Fact]
    public async Task durable_cancellation_crosses_an_actual_process_boundary()
    {
        var cancellationToken = AbortToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        using var host = fixture.BuildHost("process-cancellation-caller");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, cancellationToken);
        var jobId = Guid.NewGuid();
        await fixture.SeedTimeJobAsync(
            jobId,
            "CancellationProcessSmoke",
            (int)JobStatus.Idle,
            ownerId: null,
            cancellationToken: cancellationToken
        );
        await _MakeJobDueAsync(jobId, cancellationToken);
        await _SetCancellationRequestedAsync(jobId, cancellationToken);
        await host.StartAsync(cancellationToken);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            using var process = _StartProcess(jobId);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var output = new StringBuilder();

            try
            {
                var ready = await _ReadUntilAsync(process.StandardOutput, "HOST_READY", output, cancellationToken);
                ready.Should().BeTrue(output + await errorTask);
                var observed = await _ReadUntilAsync(process.StandardOutput, "OBSERVED", output, cancellationToken);
                observed.Should().BeTrue(output + await errorTask);
                output.ToString().Should().NotContain("USER_CODE");
                (await persistence.GetTimeJobByIdAsync(jobId, cancellationToken))!
                    .Status.Should()
                    .Be(JobStatus.Cancelled);
                await process.WaitForExitAsync(cancellationToken);
                process.ExitCode.Should().Be(0, await errorTask);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) { }
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                _ = await errorTask;
            }
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private Process _StartProcess(Guid jobId)
    {
        var fixtureAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "Headless.Jobs.CancellationProcessFixture.dll"
        );
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(fixtureAssembly);
        startInfo.ArgumentList.Add(jobId.ToString("D"));
        startInfo.Environment[_ConnectionEnvironmentVariable] = fixture.ConnectionString;

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start cancellation fixture.");
    }

    private async Task _MakeJobDueAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ExecutionTime\" = now() WHERE \"Id\" = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = jobId;
        command.Parameters.Add(parameter);
        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
    }

    private async Task _SetCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"CancelRequested\" = TRUE WHERE \"Id\" = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = jobId;
        command.Parameters.Add(parameter);
        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
    }

    private static async Task<bool> _ReadUntilAsync(
        StreamReader output,
        string expected,
        StringBuilder diagnostics,
        CancellationToken cancellationToken
    )
    {
        while (await output.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            diagnostics.AppendLine(line);
            if (string.Equals(line, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
