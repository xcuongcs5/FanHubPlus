ALTER TABLE dbo.DEVICE_TOKEN ADD binding_id uniqueidentifier NOT NULL DEFAULT NEWID();
ALTER TABLE dbo.NOTIFICATION_DELIVERY ADD lock_id uniqueidentifier NULL,
    binding_id uniqueidentifier NULL, expires_at datetime2(3) NOT NULL DEFAULT DATEADD(day,1,SYSUTCDATETIME()),
    provider_message_id varchar(250) NULL;
-- Compile this statement after ALTER TABLE, within the migration runner's transaction.
EXEC(N'UPDATE d SET binding_id=t.binding_id FROM dbo.NOTIFICATION_DELIVERY d JOIN dbo.DEVICE_TOKEN t ON t.device_token_id=d.device_token_id');
ALTER TABLE dbo.NOTIFICATION_DELIVERY ALTER COLUMN binding_id uniqueidentifier NOT NULL;
CREATE TABLE dbo.BOOKING_NOTIFICATION_STATE (
    booking_id uniqueidentifier NOT NULL PRIMARY KEY,
    user_id uniqueidentifier NOT NULL,
    status varchar(30) NOT NULL,
    source_version bigint NOT NULL CHECK(source_version > 0)
);
