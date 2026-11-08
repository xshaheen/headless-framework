// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Npgsql;

namespace Tests;

public partial class PostgreSqlStorageTests
{
    [Fact]
    public override Task should_record_a_published_receipt_with_a_null_reason_for_a_single_resend() =>
        base.should_record_a_published_receipt_with_a_null_reason_for_a_single_resend();

    [Fact]
    public override Task should_delete_a_published_row_with_a_receipt_and_record_a_refused_delete_of_a_missing_row() =>
        base.should_delete_a_published_row_with_a_receipt_and_record_a_refused_delete_of_a_missing_row();

    [Fact]
    public override Task should_leave_a_failure_written_after_confirm_out_of_a_published_scope() =>
        base.should_leave_a_failure_written_after_confirm_out_of_a_published_scope();

    [Fact]
    public override Task should_refuse_a_published_chunk_write_over_a_row_re_sent_and_failed_again_after_the_read() =>
        base.should_refuse_a_published_chunk_write_over_a_row_re_sent_and_failed_again_after_the_read();

    [Fact]
    public override Task should_refuse_a_published_write_without_a_named_operator() =>
        base.should_refuse_a_published_write_without_a_named_operator();

    [Fact]
    public override Task should_leave_a_published_row_with_a_pending_retry_untouched() =>
        base.should_leave_a_published_row_with_a_pending_retry_untouched();

    [Fact]
    public override Task should_leave_a_published_row_with_a_live_send_lease_untouched() =>
        base.should_leave_a_published_row_with_a_live_send_lease_untouched();

    [Fact]
    public override Task should_refuse_to_resend_a_published_row_that_succeeded() =>
        base.should_refuse_to_resend_a_published_row_that_succeeded();

    [Fact]
    public override Task should_replay_a_repeated_published_request_with_one_receipt() =>
        base.should_replay_a_repeated_published_request_with_one_receipt();

    [Fact]
    public override Task should_refuse_a_published_operation_id_reused_by_a_different_request() =>
        base.should_refuse_a_published_operation_id_reused_by_a_different_request();

    [Fact]
    public override Task should_record_an_additional_outbox_receipt_once_and_refuse_its_operation_id_for_another_outbox() =>
        base.should_record_an_additional_outbox_receipt_once_and_refuse_its_operation_id_for_another_outbox();

    [Fact]
    public override Task should_refuse_a_published_write_without_a_receipt_on_the_store_that_holds_the_ledger() =>
        base.should_refuse_a_published_write_without_a_receipt_on_the_store_that_holds_the_ledger();

    [Fact]
    public override Task should_scope_a_published_delete_to_both_terminal_statuses_and_a_resend_to_failures() =>
        base.should_scope_a_published_delete_to_both_terminal_statuses_and_a_resend_to_failures();

    protected override async Task SetPublishedLeaseAsync(
        Guid storageId,
        TimeSpan liveFor,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """UPDATE headless.messaging_published SET "locked_until"=clock_timestamp()+@LiveFor, "owner"='another-node' WHERE "id"=@Id;""",
                new { Id = storageId, LiveFor = liveFor },
                cancellationToken: cancellationToken
            )
        );
    }
}
