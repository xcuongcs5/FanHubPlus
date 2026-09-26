CREATE TABLE dbo.EVENT_PROJECTION (
    event_id uniqueidentifier NOT NULL CONSTRAINT PK_EVENT_PROJECTION PRIMARY KEY,
    organizer_id uniqueidentifier NOT NULL,
    title nvarchar(250) NOT NULL,
    start_time datetime2(3) NOT NULL,
    end_time datetime2(3) NOT NULL,
    capacity int NOT NULL,
    status varchar(20) NOT NULL,
    source_version bigint NOT NULL DEFAULT 0,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_EVENT_PROJECTION_capacity CHECK(capacity > 0),
    CONSTRAINT CK_EVENT_PROJECTION_dates CHECK(end_time > start_time)
);
CREATE TABLE dbo.EVENT_STAFF_PROJECTION (
    event_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NOT NULL,
    role varchar(20) NOT NULL,
    is_active bit NOT NULL,
    source_version bigint NOT NULL DEFAULT 0,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_EVENT_STAFF_PROJECTION PRIMARY KEY(event_id, user_id)
);
CREATE TABLE dbo.TICKET_TYPE (
    ticket_type_id uniqueidentifier NOT NULL CONSTRAINT PK_TICKET_TYPE PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    event_id uniqueidentifier NOT NULL REFERENCES dbo.EVENT_PROJECTION(event_id),
    name nvarchar(100) NOT NULL,
    price decimal(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'VND',
    tier_rank int NOT NULL DEFAULT 0,
    total_quantity int NOT NULL,
    reserved_quantity int NOT NULL DEFAULT 0,
    sold_quantity int NOT NULL DEFAULT 0,
    sale_start datetime2(3) NOT NULL,
    sale_end datetime2(3) NOT NULL,
    is_active bit NOT NULL DEFAULT 1,
    version rowversion NOT NULL,
    CONSTRAINT UQ_TICKET_TYPE_name UNIQUE(event_id, name),
    CONSTRAINT UQ_TICKET_TYPE_event UNIQUE(event_id, ticket_type_id),
    CONSTRAINT CK_TICKET_TYPE_price CHECK(price >= 0 AND currency COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'),
    CONSTRAINT CK_TICKET_TYPE_stock CHECK(total_quantity >= 0 AND reserved_quantity >= 0 AND sold_quantity >= 0 AND reserved_quantity + CONVERT(bigint,sold_quantity) <= total_quantity),
    CONSTRAINT CK_TICKET_TYPE_dates CHECK(sale_end > sale_start),
    CONSTRAINT CK_TICKET_TYPE_rank CHECK(tier_rank >= 0)
);
CREATE TABLE dbo.BOOKING_REQUEST (
    request_id uniqueidentifier NOT NULL CONSTRAINT PK_BOOKING_REQUEST PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    user_id uniqueidentifier NOT NULL,
    idempotency_key varchar(100) NOT NULL,
    request_hash binary(32) NOT NULL,
    event_id uniqueidentifier NOT NULL,
    ticket_type_id uniqueidentifier NOT NULL,
    quantity int NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Pending',
    failure_code varchar(100) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    completed_at datetime2(3) NULL,
    version rowversion NOT NULL,
    CONSTRAINT UQ_BOOKING_REQUEST_idempotency UNIQUE(user_id, idempotency_key),
    CONSTRAINT UQ_BOOKING_REQUEST_type UNIQUE(request_id, event_id, ticket_type_id),
    CONSTRAINT FK_BOOKING_REQUEST_type FOREIGN KEY(event_id,ticket_type_id) REFERENCES dbo.TICKET_TYPE(event_id,ticket_type_id),
    CONSTRAINT CK_BOOKING_REQUEST_quantity CHECK(quantity BETWEEN 1 AND 100),
    CONSTRAINT CK_BOOKING_REQUEST_status CHECK(status IN ('Pending','Processing','Completed','Failed','Expired'))
);
CREATE INDEX IX_BOOKING_REQUEST_queue ON dbo.BOOKING_REQUEST(status, created_at);
-- One row represents one ticket, including when a request purchases several tickets.
CREATE TABLE dbo.TICKET_BOOKING (
    booking_id uniqueidentifier NOT NULL CONSTRAINT PK_TICKET_BOOKING PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    request_id uniqueidentifier NOT NULL,
    ticket_number int NOT NULL,
    event_id uniqueidentifier NOT NULL,
    ticket_type_id uniqueidentifier NOT NULL,
    purchaser_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NOT NULL, -- current owner, changes on confirmed transfer
    unit_price decimal(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'VND',
    status varchar(30) NOT NULL DEFAULT 'Reserved',
    expires_at datetime2(3) NOT NULL,
    nft_token_id varchar(200) NULL,
    blockchain_tx_hash varchar(100) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    paid_at datetime2(3) NULL,
    cancelled_at datetime2(3) NULL,
    checked_in_at datetime2(3) NULL,
    version rowversion NOT NULL,
    CONSTRAINT UQ_TICKET_BOOKING_number UNIQUE(request_id, ticket_number),
    CONSTRAINT UQ_TICKET_BOOKING_event UNIQUE(booking_id, event_id),
    CONSTRAINT FK_TICKET_BOOKING_request FOREIGN KEY(request_id) REFERENCES dbo.BOOKING_REQUEST(request_id),
    CONSTRAINT FK_TICKET_BOOKING_type FOREIGN KEY(event_id,ticket_type_id) REFERENCES dbo.TICKET_TYPE(event_id,ticket_type_id),
    CONSTRAINT CK_TICKET_BOOKING_price CHECK(unit_price >= 0),
    CONSTRAINT CK_TICKET_BOOKING_number CHECK(ticket_number > 0),
    CONSTRAINT CK_TICKET_BOOKING_expiry CHECK(expires_at > created_at),
    CONSTRAINT CK_TICKET_BOOKING_status CHECK(status IN ('Reserved','PaymentPending','Paid','MintPending','Active','TransferPending','UpgradePending','CheckedIn','Cancelled','Expired','RefundPending','Refunded'))
);
CREATE UNIQUE INDEX UX_TICKET_BOOKING_nft ON dbo.TICKET_BOOKING(nft_token_id) WHERE nft_token_id IS NOT NULL;
CREATE INDEX IX_TICKET_BOOKING_owner ON dbo.TICKET_BOOKING(user_id, created_at DESC, booking_id);
CREATE INDEX IX_TICKET_BOOKING_event ON dbo.TICKET_BOOKING(event_id, status, booking_id);
CREATE INDEX IX_TICKET_BOOKING_expiry ON dbo.TICKET_BOOKING(status, expires_at);
CREATE TABLE dbo.TICKET_TRANSFER (
    transfer_id uniqueidentifier NOT NULL CONSTRAINT PK_TICKET_TRANSFER PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    booking_id uniqueidentifier NOT NULL REFERENCES dbo.TICKET_BOOKING(booking_id),
    from_user_id uniqueidentifier NOT NULL,
    to_user_id uniqueidentifier NOT NULL,
    idempotency_key varchar(100) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Pending',
    blockchain_tx_hash varchar(100) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    completed_at datetime2(3) NULL,
    CONSTRAINT UQ_TICKET_TRANSFER_request UNIQUE(from_user_id, idempotency_key),
    CONSTRAINT CK_TICKET_TRANSFER_users CHECK(from_user_id <> to_user_id),
    CONSTRAINT CK_TICKET_TRANSFER_status CHECK(status IN ('Pending','Completed','Failed'))
);
CREATE UNIQUE INDEX UX_TICKET_TRANSFER_pending ON dbo.TICKET_TRANSFER(booking_id) WHERE status = 'Pending';
CREATE TABLE dbo.TICKET_UPGRADE (
    upgrade_id uniqueidentifier NOT NULL CONSTRAINT PK_TICKET_UPGRADE PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    booking_id uniqueidentifier NOT NULL,
    event_id uniqueidentifier NOT NULL,
    from_ticket_type_id uniqueidentifier NOT NULL,
    to_ticket_type_id uniqueidentifier NOT NULL,
    price_difference decimal(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'VND',
    idempotency_key varchar(100) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Pending',
    expires_at datetime2(3) NOT NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    completed_at datetime2(3) NULL,
    CONSTRAINT UQ_TICKET_UPGRADE_request UNIQUE(booking_id, idempotency_key),
    CONSTRAINT FK_TICKET_UPGRADE_booking FOREIGN KEY(booking_id,event_id) REFERENCES dbo.TICKET_BOOKING(booking_id,event_id),
    CONSTRAINT FK_TICKET_UPGRADE_from FOREIGN KEY(event_id,from_ticket_type_id) REFERENCES dbo.TICKET_TYPE(event_id,ticket_type_id),
    CONSTRAINT FK_TICKET_UPGRADE_to FOREIGN KEY(event_id,to_ticket_type_id) REFERENCES dbo.TICKET_TYPE(event_id,ticket_type_id),
    CONSTRAINT CK_TICKET_UPGRADE_price CHECK(price_difference >= 0 AND from_ticket_type_id <> to_ticket_type_id),
    CONSTRAINT CK_TICKET_UPGRADE_status CHECK(status IN ('Pending','Completed','Failed','Expired')),
    CONSTRAINT CK_TICKET_UPGRADE_expiry CHECK(expires_at > created_at)
);
CREATE UNIQUE INDEX UX_TICKET_UPGRADE_pending ON dbo.TICKET_UPGRADE(booking_id) WHERE status = 'Pending';
CREATE TABLE dbo.TICKET_CHECK_IN (
    booking_id uniqueidentifier NOT NULL CONSTRAINT PK_TICKET_CHECK_IN PRIMARY KEY REFERENCES dbo.TICKET_BOOKING(booking_id),
    staff_user_id uniqueidentifier NOT NULL,
    qr_payload_hash binary(32) NOT NULL,
    checked_in_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_TICKET_CHECK_IN_payload UNIQUE(qr_payload_hash)
);
