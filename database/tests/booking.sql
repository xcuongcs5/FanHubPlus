SET NOCOUNT ON;
SET XACT_ABORT OFF;
BEGIN TRANSACTION;
DECLARE @event uniqueidentifier=NEWID(), @type uniqueidentifier=NEWID(), @request uniqueidentifier=NEWID(),
    @booking uniqueidentifier=NEWID(), @user uniqueidentifier=NEWID(), @hash binary(32)=HASHBYTES('SHA2_256','test');
INSERT dbo.EVENT_PROJECTION(event_id,organizer_id,title,start_time,end_time,capacity,status)
VALUES(@event,@user,N'Test','2030-01-01','2030-01-02',1,'Published');
INSERT dbo.TICKET_TYPE(ticket_type_id,event_id,name,price,total_quantity,sale_start,sale_end)
VALUES(@type,@event,N'VIP',100000,1,'2029-01-01','2030-01-01');
-- Atomic stock guard: only the first reservation for the final ticket may succeed.
UPDATE dbo.TICKET_TYPE SET reserved_quantity=reserved_quantity+1
WHERE ticket_type_id=@type AND total_quantity-reserved_quantity-sold_quantity >= 1;
IF @@ROWCOUNT <> 1 THROW 51000, 'First reservation failed', 1;
UPDATE dbo.TICKET_TYPE SET reserved_quantity=reserved_quantity+1
WHERE ticket_type_id=@type AND total_quantity-reserved_quantity-sold_quantity >= 1;
IF @@ROWCOUNT <> 0 THROW 51000, 'Stock guard oversold final ticket', 1;
BEGIN TRY
    UPDATE dbo.TICKET_TYPE SET sold_quantity=1 WHERE ticket_type_id=@type;
    THROW 51000, 'Overselling constraint did not fire', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
INSERT dbo.BOOKING_REQUEST(request_id,user_id,idempotency_key,request_hash,event_id,ticket_type_id,quantity)
VALUES(@request,@user,'test',@hash,@event,@type,1);
BEGIN TRY
    INSERT dbo.BOOKING_REQUEST(user_id,idempotency_key,request_hash,event_id,ticket_type_id,quantity)
    VALUES(@user,'test',@hash,@event,@type,1);
    THROW 51000, 'Duplicate booking request accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
INSERT dbo.TICKET_BOOKING(booking_id,request_id,ticket_number,event_id,ticket_type_id,purchaser_id,user_id,unit_price,expires_at)
VALUES(@booking,@request,1,@event,@type,@user,@user,100000,DATEADD(minute,15,SYSUTCDATETIME()));
INSERT dbo.TICKET_CHECK_IN(booking_id,staff_user_id,qr_payload_hash) VALUES(@booking,@user,@hash);
BEGIN TRY
    INSERT dbo.TICKET_CHECK_IN(booking_id,staff_user_id,qr_payload_hash) VALUES(@booking,@user,@hash);
    THROW 51000, 'Duplicate check-in accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
DECLARE @other_event uniqueidentifier=NEWID(), @other_type uniqueidentifier=NEWID();
INSERT dbo.EVENT_PROJECTION(event_id,organizer_id,title,start_time,end_time,capacity,status)
VALUES(@other_event,@user,N'Other','2030-01-01','2030-01-02',1,'Published');
INSERT dbo.TICKET_TYPE(ticket_type_id,event_id,name,price,total_quantity,sale_start,sale_end)
VALUES(@other_type,@other_event,N'VIP',100000,1,'2029-01-01','2030-01-01');
BEGIN TRY
    UPDATE dbo.TICKET_BOOKING SET event_id=@other_event,ticket_type_id=@other_type WHERE booking_id=@booking;
    THROW 51000, 'Ticket moved outside original request event', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
ROLLBACK;
PRINT 'PASS booking: inventory guard, stock constraint, idempotency, check-in, event consistency';
