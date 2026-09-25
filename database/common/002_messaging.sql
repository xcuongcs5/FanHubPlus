-- Independently created in each service database, never a shared database/table.
CREATE TABLE dbo.USER_PROJECTION (
    user_id uniqueidentifier NOT NULL CONSTRAINT PK_USER_PROJECTION PRIMARY KEY,
    full_name nvarchar(200) NOT NULL,
    avatar_url nvarchar(500) NULL,
    status varchar(20) NOT NULL DEFAULT 'Active',
    source_updated_at datetime2(3) NOT NULL,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_USER_PROJECTION_status CHECK(status IN ('Active','Banned','Deleted'))
);
CREATE TABLE dbo.OUTBOX_MESSAGE (
    message_id uniqueidentifier NOT NULL CONSTRAINT PK_OUTBOX_MESSAGE PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    event_type varchar(200) NOT NULL,
    schema_version int NOT NULL DEFAULT 1,
    aggregate_id uniqueidentifier NOT NULL,
    aggregate_version bigint NOT NULL,
    correlation_id uniqueidentifier NULL,
    payload nvarchar(max) NOT NULL,
    occurred_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    published_at datetime2(3) NULL,
    attempt_count int NOT NULL DEFAULT 0,
    next_attempt_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    locked_until datetime2(3) NULL,
    lock_id uniqueidentifier NULL,
    last_error nvarchar(2000) NULL,
    CONSTRAINT UQ_OUTBOX_MESSAGE_version UNIQUE(aggregate_id,aggregate_version,event_type),
    CONSTRAINT CK_OUTBOX_MESSAGE_payload CHECK(ISJSON(payload) = 1),
    CONSTRAINT CK_OUTBOX_MESSAGE_version CHECK(schema_version > 0 AND aggregate_version > 0 AND attempt_count >= 0)
);
CREATE INDEX IX_OUTBOX_MESSAGE_pending ON dbo.OUTBOX_MESSAGE(next_attempt_at, occurred_at) WHERE published_at IS NULL;
CREATE TABLE dbo.INBOX_MESSAGE (
    consumer varchar(150) NOT NULL,
    message_id uniqueidentifier NOT NULL,
    processed_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_INBOX_MESSAGE PRIMARY KEY(consumer,message_id)
);
