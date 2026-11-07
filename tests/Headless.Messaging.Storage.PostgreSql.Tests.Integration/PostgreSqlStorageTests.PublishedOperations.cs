// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
