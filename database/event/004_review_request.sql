-- Correlate moderation decisions with the current submission, not just the event ID.
ALTER TABLE dbo.EVENT ADD review_request_id uniqueidentifier NULL;
ALTER TABLE dbo.EVENT_REVIEW ADD review_request_id uniqueidentifier NULL;
