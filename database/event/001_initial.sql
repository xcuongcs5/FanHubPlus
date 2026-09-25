-- Event owns locations/events. Identity, category and booking data are local projections.
CREATE TABLE dbo.LOCATION (
    location_id uniqueidentifier NOT NULL CONSTRAINT PK_LOCATION PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    name nvarchar(200) NOT NULL,
    address nvarchar(500) NOT NULL,
    latitude decimal(9,6) NOT NULL,
    longitude decimal(9,6) NOT NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_LOCATION_coordinates CHECK (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)
);
CREATE TABLE dbo.EVENT (
    event_id uniqueidentifier NOT NULL CONSTRAINT PK_EVENT PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    organizer_id uniqueidentifier NOT NULL, -- Identity UUID; no cross-database FK
    location_id uniqueidentifier NOT NULL REFERENCES dbo.LOCATION(location_id),
    title nvarchar(250) NOT NULL,
    description nvarchar(max) NOT NULL,
    banner_url nvarchar(2048) NULL,
    start_time datetime2(3) NOT NULL,
    end_time datetime2(3) NOT NULL,
    capacity int NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Draft',
    is_featured bit NOT NULL DEFAULT 0,
    published_at datetime2(3) NULL,
    cancelled_at datetime2(3) NULL,
    cancellation_reason nvarchar(1000) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetime2(3) NULL,
    version rowversion NOT NULL,
    CONSTRAINT CK_EVENT_time CHECK (end_time > start_time),
    CONSTRAINT CK_EVENT_capacity CHECK (capacity > 0),
    CONSTRAINT CK_EVENT_status CHECK (status IN ('Draft','PendingReview','Approved','Rejected','Published','Cancelled','Completed'))
);
CREATE INDEX IX_EVENT_feed ON dbo.EVENT(status, start_time, event_id) INCLUDE(title, location_id, is_featured);
CREATE INDEX IX_EVENT_organizer ON dbo.EVENT(organizer_id, created_at DESC, event_id);
CREATE INDEX IX_EVENT_featured ON dbo.EVENT(is_featured, status, start_time);
CREATE INDEX IX_EVENT_location ON dbo.EVENT(location_id);
CREATE TABLE dbo.CATEGORY_PROJECTION (
    category_id uniqueidentifier NOT NULL CONSTRAINT PK_CATEGORY_PROJECTION PRIMARY KEY,
    parent_id uniqueidentifier NULL, -- logical parent; tolerate out-of-order messages
    name nvarchar(200) NOT NULL,
    is_deleted bit NOT NULL DEFAULT 0,
    source_version bigint NOT NULL DEFAULT 0,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.EVENT_CATEGORY (
    event_id uniqueidentifier NOT NULL REFERENCES dbo.EVENT(event_id),
    category_id uniqueidentifier NOT NULL REFERENCES dbo.CATEGORY_PROJECTION(category_id),
    CONSTRAINT PK_EVENT_CATEGORY PRIMARY KEY(event_id, category_id)
);
CREATE INDEX IX_EVENT_CATEGORY_category ON dbo.EVENT_CATEGORY(category_id, event_id);
CREATE TABLE dbo.EVENT_STAFF (
    event_id uniqueidentifier NOT NULL REFERENCES dbo.EVENT(event_id),
    user_id uniqueidentifier NOT NULL,
    role varchar(20) NOT NULL DEFAULT 'CheckIn',
    is_active bit NOT NULL DEFAULT 1,
    assigned_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_EVENT_STAFF PRIMARY KEY(event_id, user_id),
    CONSTRAINT CK_EVENT_STAFF_role CHECK(role IN ('CheckIn','Manager'))
);
CREATE TABLE dbo.EVENT_REVIEW (
    review_id uniqueidentifier NOT NULL CONSTRAINT PK_EVENT_REVIEW PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    event_id uniqueidentifier NOT NULL REFERENCES dbo.EVENT(event_id),
    reviewer_id uniqueidentifier NULL,
    source varchar(10) NOT NULL,
    decision varchar(20) NOT NULL,
    reason nvarchar(2000) NULL,
    created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_EVENT_REVIEW_source CHECK(source IN ('AI','Admin')),
    CONSTRAINT CK_EVENT_REVIEW_decision CHECK(decision IN ('Approved','Rejected','Flagged'))
);
CREATE INDEX IX_EVENT_REVIEW_event ON dbo.EVENT_REVIEW(event_id, created_at);
CREATE TABLE dbo.ATTENDEE_PROJECTION (
    booking_id uniqueidentifier NOT NULL CONSTRAINT PK_ATTENDEE_PROJECTION PRIMARY KEY,
    event_id uniqueidentifier NOT NULL, -- projection can arrive before event restoration
    user_id uniqueidentifier NOT NULL,
    status varchar(30) NOT NULL,
    checked_in_at datetime2(3) NULL,
    source_version bigint NOT NULL DEFAULT 0,
    synced_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_ATTENDEE_event ON dbo.ATTENDEE_PROJECTION(event_id, status, user_id);
