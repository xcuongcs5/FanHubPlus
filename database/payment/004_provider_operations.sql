-- Durable provider operations: an ambiguous refund must never be retried as a new transfer.
ALTER TABLE dbo.PAYMENT_REFUND ADD
    attempted_at datetime2(3) NULL,
    last_error varchar(100) NULL,
    next_query_at datetime2(3) NULL;
ALTER TABLE dbo.PAYMENT_TRANSACTION ADD
    next_query_at datetime2(3) NULL,
    query_attempts int NOT NULL CONSTRAINT DF_PAYMENT_query_attempts DEFAULT 0;
CREATE INDEX IX_PAYMENT_reconciliation ON dbo.PAYMENT_TRANSACTION(status,next_query_at);
CREATE INDEX IX_REFUND_operations ON dbo.PAYMENT_REFUND(status,next_query_at);
