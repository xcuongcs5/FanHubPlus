SET NOCOUNT ON;
SET XACT_ABORT OFF;
BEGIN TRANSACTION;
DECLARE @location uniqueidentifier = NEWID(), @event uniqueidentifier = NEWID(), @user uniqueidentifier = NEWID();
INSERT dbo.LOCATION(location_id,name,address,latitude,longitude) VALUES(@location,N'Test',N'Test',10.7,106.7);
INSERT dbo.EVENT(event_id,organizer_id,location_id,title,description,start_time,end_time,capacity)
VALUES(@event,@user,@location,N'Test',N'Test','2030-01-01','2030-01-02',100);
BEGIN TRY
    UPDATE dbo.LOCATION SET latitude=91 WHERE location_id=@location;
    THROW 51000, 'Invalid latitude accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
BEGIN TRY
    UPDATE dbo.EVENT SET end_time=start_time WHERE event_id=@event;
    THROW 51000, 'Invalid event dates accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
BEGIN TRY
    UPDATE dbo.EVENT SET capacity=0 WHERE event_id=@event;
    THROW 51000, 'Zero event capacity accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
INSERT dbo.EVENT_STAFF(event_id,user_id) VALUES(@event,@user);
BEGIN TRY
    INSERT dbo.EVENT_STAFF(event_id,user_id) VALUES(@event,@user);
    THROW 51000, 'Duplicate staff assignment accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
BEGIN TRY
    DELETE dbo.LOCATION WHERE location_id=@location;
    THROW 51000, 'Referenced location deleted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
ROLLBACK;
PRINT 'PASS event: dates, coordinates, capacity, staff uniqueness, local FK';
