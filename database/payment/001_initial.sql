CREATE TABLE dbo.BOOKING_PROJECTION (
    booking_id uniqueidentifier NOT NULL CONSTRAINT PK_BOOKING_PROJECTION PRIMARY KEY,
    user_id uniqueidentifier NOT NULL,
    event_id uniqueidentifier NOT NULL,
    amount decimal(19,4) NOT NULL,
    currency char(3) NOT NULL,
    status varchar(30) NOT NULL,
    expires_at datetime2(3) NOT NULL,
    source_version bigint NOT NULL DEFAULT 0,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_BOOKING_PROJECTION_amount CHECK(amount >= 0)
);
CREATE TABLE dbo.WALLET (
    wallet_id uniqueidentifier NOT NULL CONSTRAINT PK_WALLET PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    user_id uniqueidentifier NOT NULL,
    currency char(3) NOT NULL DEFAULT 'VND',
    balance decimal(19,4) NOT NULL DEFAULT 0,
    status varchar(10) NOT NULL DEFAULT 'Active',
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetime2(3) NULL,
    version rowversion NOT NULL,
    CONSTRAINT UQ_WALLET_user_currency UNIQUE(user_id,currency),
    CONSTRAINT CK_WALLET_balance CHECK(balance >= 0),
    CONSTRAINT CK_WALLET_status CHECK(status IN ('Active','Frozen','Closed'))
);
CREATE TABLE dbo.PAYMENT_TRANSACTION (
    transaction_id uniqueidentifier NOT NULL CONSTRAINT PK_PAYMENT_TRANSACTION PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    user_id uniqueidentifier NOT NULL,
    booking_id uniqueidentifier NULL REFERENCES dbo.BOOKING_PROJECTION(booking_id),
    wallet_id uniqueidentifier NULL REFERENCES dbo.WALLET(wallet_id),
    upgrade_id uniqueidentifier NULL, -- logical Booking reference
    purpose varchar(20) NOT NULL,
    provider varchar(10) NOT NULL,
    merchant_reference varchar(100) NOT NULL,
    provider_transaction_id varchar(150) NULL,
    idempotency_key varchar(100) NOT NULL,
    request_hash binary(32) NOT NULL,
    amount decimal(19,4) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'VND',
    status varchar(20) NOT NULL DEFAULT 'Pending',
    checkout_url nvarchar(2048) NULL,
    expires_at datetime2(3) NOT NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    completed_at datetime2(3) NULL,
    version rowversion NOT NULL,
    CONSTRAINT UQ_PAYMENT_TRANSACTION_reference UNIQUE(merchant_reference),
    CONSTRAINT UQ_PAYMENT_TRANSACTION_idempotency UNIQUE(user_id,idempotency_key),
    CONSTRAINT CK_PAYMENT_TRANSACTION_amount CHECK(amount > 0),
    CONSTRAINT CK_PAYMENT_TRANSACTION_provider CHECK(provider IN ('VNPay','MoMo','Wallet')),
    CONSTRAINT CK_PAYMENT_TRANSACTION_status CHECK(status IN ('Pending','Processing','Succeeded','Failed','Expired','Cancelled')),
    CONSTRAINT CK_PAYMENT_TRANSACTION_target CHECK(
        (purpose = 'Booking' AND booking_id IS NOT NULL AND upgrade_id IS NULL) OR
        (purpose = 'Upgrade' AND booking_id IS NOT NULL AND upgrade_id IS NOT NULL) OR
        (purpose = 'Deposit' AND wallet_id IS NOT NULL AND booking_id IS NULL AND upgrade_id IS NULL AND provider <> 'Wallet')),
    CONSTRAINT CK_PAYMENT_TRANSACTION_wallet CHECK(provider <> 'Wallet' OR wallet_id IS NOT NULL),
    CONSTRAINT CK_PAYMENT_TRANSACTION_expiry CHECK(expires_at > created_at)
);
CREATE UNIQUE INDEX UX_PAYMENT_provider_transaction ON dbo.PAYMENT_TRANSACTION(provider,provider_transaction_id) WHERE provider_transaction_id IS NOT NULL;
CREATE UNIQUE INDEX UX_PAYMENT_booking_success ON dbo.PAYMENT_TRANSACTION(booking_id) WHERE purpose = 'Booking' AND status = 'Succeeded';
CREATE UNIQUE INDEX UX_PAYMENT_upgrade_success ON dbo.PAYMENT_TRANSACTION(upgrade_id) WHERE purpose = 'Upgrade' AND status = 'Succeeded';
CREATE INDEX IX_PAYMENT_booking ON dbo.PAYMENT_TRANSACTION(booking_id, created_at DESC);
CREATE INDEX IX_PAYMENT_user ON dbo.PAYMENT_TRANSACTION(user_id, created_at DESC, transaction_id);
CREATE TABLE dbo.PAYMENT_WEBHOOK (
    webhook_id uniqueidentifier NOT NULL CONSTRAINT PK_PAYMENT_WEBHOOK PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    provider varchar(10) NOT NULL,
    provider_event_key varchar(200) NOT NULL,
    transaction_id uniqueidentifier NULL REFERENCES dbo.PAYMENT_TRANSACTION(transaction_id),
    payload_hash binary(32) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Received',
    failure_reason nvarchar(1000) NULL,
    received_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    processed_at datetime2(3) NULL,
    CONSTRAINT UQ_PAYMENT_WEBHOOK_event UNIQUE(provider,provider_event_key),
    CONSTRAINT CK_PAYMENT_WEBHOOK_provider CHECK(provider IN ('VNPay','MoMo')),
    CONSTRAINT CK_PAYMENT_WEBHOOK_status CHECK(status IN ('Received','Processed','Rejected','Failed'))
);
CREATE TABLE dbo.PAYMENT_REFUND (
    refund_id uniqueidentifier NOT NULL CONSTRAINT PK_PAYMENT_REFUND PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    transaction_id uniqueidentifier NOT NULL REFERENCES dbo.PAYMENT_TRANSACTION(transaction_id),
    requested_by uniqueidentifier NOT NULL,
    idempotency_key varchar(100) NOT NULL,
    amount decimal(19,4) NOT NULL,
    reason nvarchar(1000) NOT NULL,
    provider_refund_id varchar(150) NULL,
    status varchar(20) NOT NULL DEFAULT 'Pending',
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    completed_at datetime2(3) NULL,
    version rowversion NOT NULL,
    CONSTRAINT UQ_PAYMENT_REFUND_request UNIQUE(transaction_id,idempotency_key),
    CONSTRAINT CK_PAYMENT_REFUND_amount CHECK(amount > 0),
    CONSTRAINT CK_PAYMENT_REFUND_status CHECK(status IN ('Pending','Processing','Succeeded','Failed'))
);
CREATE TABLE dbo.WALLET_LEDGER (
    ledger_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WALLET_LEDGER PRIMARY KEY,
    wallet_id uniqueidentifier NOT NULL REFERENCES dbo.WALLET(wallet_id),
    transaction_id uniqueidentifier NOT NULL REFERENCES dbo.PAYMENT_TRANSACTION(transaction_id),
    refund_id uniqueidentifier NULL REFERENCES dbo.PAYMENT_REFUND(refund_id),
    entry_type varchar(10) NOT NULL,
    amount decimal(19,4) NOT NULL,
    balance_after decimal(19,4) NOT NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_WALLET_LEDGER_amount CHECK(amount > 0 AND balance_after >= 0),
    CONSTRAINT CK_WALLET_LEDGER_type CHECK((entry_type IN ('Deposit','Payment') AND refund_id IS NULL) OR (entry_type = 'Refund' AND refund_id IS NOT NULL))
);
CREATE UNIQUE INDEX UX_WALLET_LEDGER_payment ON dbo.WALLET_LEDGER(transaction_id,entry_type) WHERE refund_id IS NULL;
CREATE UNIQUE INDEX UX_WALLET_LEDGER_refund ON dbo.WALLET_LEDGER(refund_id) WHERE refund_id IS NOT NULL;
CREATE INDEX IX_WALLET_LEDGER_history ON dbo.WALLET_LEDGER(wallet_id, created_at DESC, ledger_id DESC);
