CREATE TABLE dbo.NOTIFICATION (
    notification_id uniqueidentifier NOT NULL CONSTRAINT PK_NOTIFICATION PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    user_id uniqueidentifier NOT NULL,
    source_message_id uniqueidentifier NOT NULL,
    type varchar(100) NOT NULL,
    title nvarchar(250) NOT NULL,
    message nvarchar(4000) NOT NULL,
    data_json nvarchar(max) NULL,
    read_at datetime2(3) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_NOTIFICATION_source UNIQUE(user_id,source_message_id,type),
    CONSTRAINT CK_NOTIFICATION_json CHECK(data_json IS NULL OR ISJSON(data_json) = 1)
);
CREATE INDEX IX_NOTIFICATION_user ON dbo.NOTIFICATION(user_id, created_at DESC, notification_id) INCLUDE(read_at,title,type);
CREATE INDEX IX_NOTIFICATION_unread ON dbo.NOTIFICATION(user_id, created_at DESC) WHERE read_at IS NULL;
CREATE TABLE dbo.DEVICE_TOKEN (
    device_token_id uniqueidentifier NOT NULL CONSTRAINT PK_DEVICE_TOKEN PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    user_id uniqueidentifier NOT NULL,
    provider varchar(10) NOT NULL,
    device_id varchar(200) NOT NULL,
    token nvarchar(2048) NOT NULL,
    token_hash AS CONVERT(binary(32), HASHBYTES('SHA2_256', token)) PERSISTED,
    is_active bit NOT NULL DEFAULT 1,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    last_seen_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_DEVICE_TOKEN_provider CHECK(provider IN ('FCM','APNs')),
    CONSTRAINT CK_DEVICE_TOKEN_value CHECK(LEN(token) > 0),
    CONSTRAINT UQ_DEVICE_TOKEN_device UNIQUE(provider,device_id)
);
CREATE UNIQUE INDEX UX_DEVICE_TOKEN_token ON dbo.DEVICE_TOKEN(provider,token_hash);
CREATE INDEX IX_DEVICE_TOKEN_user ON dbo.DEVICE_TOKEN(user_id,is_active);
CREATE TABLE dbo.NOTIFICATION_DELIVERY (
    delivery_id uniqueidentifier NOT NULL CONSTRAINT PK_NOTIFICATION_DELIVERY PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    notification_id uniqueidentifier NOT NULL REFERENCES dbo.NOTIFICATION(notification_id),
    device_token_id uniqueidentifier NOT NULL REFERENCES dbo.DEVICE_TOKEN(device_token_id),
    status varchar(20) NOT NULL DEFAULT 'Pending',
    attempt_count int NOT NULL DEFAULT 0,
    next_attempt_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    locked_until datetime2(3) NULL,
    last_error nvarchar(2000) NULL,
    sent_at datetime2(3) NULL,
    CONSTRAINT UQ_NOTIFICATION_DELIVERY_target UNIQUE(notification_id,device_token_id),
    CONSTRAINT CK_NOTIFICATION_DELIVERY_attempts CHECK(attempt_count >= 0),
    CONSTRAINT CK_NOTIFICATION_DELIVERY_status CHECK(status IN ('Pending','Processing','Sent','Failed','Skipped'))
);
CREATE INDEX IX_NOTIFICATION_DELIVERY_pending ON dbo.NOTIFICATION_DELIVERY(status,next_attempt_at);
