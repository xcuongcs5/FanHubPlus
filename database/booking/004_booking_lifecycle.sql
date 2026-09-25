ALTER TABLE dbo.TICKET_TYPE ADD source_version bigint NOT NULL CONSTRAINT DF_TICKET_TYPE_source_version DEFAULT 0;
ALTER TABLE dbo.TICKET_BOOKING ADD blockchain_error nvarchar(1000) NULL;
CREATE TABLE dbo.BOOKING_PAYMENT (
    transaction_id uniqueidentifier NOT NULL CONSTRAINT PK_BOOKING_PAYMENT PRIMARY KEY,
    booking_id uniqueidentifier NOT NULL REFERENCES dbo.TICKET_BOOKING(booking_id),
    upgrade_id uniqueidentifier NULL REFERENCES dbo.TICKET_UPGRADE(upgrade_id),
    amount decimal(19,4) NOT NULL,
    currency char(3) NOT NULL,
    status varchar(20) NOT NULL,
    refund_reason nvarchar(500) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_BOOKING_PAYMENT_amount CHECK(amount > 0),
    CONSTRAINT CK_BOOKING_PAYMENT_status CHECK(status IN ('Applied','RefundPending','Refunded','RefundFailed'))
);
CREATE INDEX IX_BOOKING_PAYMENT_booking ON dbo.BOOKING_PAYMENT(booking_id,status);
