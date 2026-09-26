-- Trusted quote supplied by Booking through RabbitMQ; never take price from the browser.
CREATE TABLE dbo.UPGRADE_PROJECTION (
    upgrade_id uniqueidentifier NOT NULL CONSTRAINT PK_UPGRADE_PROJECTION PRIMARY KEY,
    booking_id uniqueidentifier NOT NULL REFERENCES dbo.BOOKING_PROJECTION(booking_id),
    user_id uniqueidentifier NOT NULL,
    amount decimal(19,4) NOT NULL,
    currency char(3) NOT NULL,
    status varchar(20) NOT NULL,
    expires_at datetime2(3) NOT NULL,
    source_version bigint NOT NULL DEFAULT 0,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_UPGRADE_PROJECTION_booking UNIQUE(upgrade_id,booking_id),
    CONSTRAINT CK_UPGRADE_PROJECTION_amount CHECK(amount >= 0)
);
ALTER TABLE dbo.PAYMENT_TRANSACTION ADD CONSTRAINT FK_PAYMENT_TRANSACTION_upgrade
    FOREIGN KEY(upgrade_id,booking_id) REFERENCES dbo.UPGRADE_PROJECTION(upgrade_id,booking_id);
