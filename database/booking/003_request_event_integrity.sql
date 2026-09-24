-- A ticket may change type after an upgrade, but must remain in the request's event.
ALTER TABLE dbo.BOOKING_REQUEST ADD CONSTRAINT UQ_BOOKING_REQUEST_event UNIQUE(request_id,event_id);
ALTER TABLE dbo.TICKET_BOOKING DROP CONSTRAINT FK_TICKET_BOOKING_request;
ALTER TABLE dbo.TICKET_BOOKING ADD CONSTRAINT FK_TICKET_BOOKING_request
    FOREIGN KEY(request_id,event_id) REFERENCES dbo.BOOKING_REQUEST(request_id,event_id);
