SET NOCOUNT ON;
SET XACT_ABORT OFF;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
DECLARE @user uniqueidentifier=NEWID(), @message uniqueidentifier=NEWID(), @notification uniqueidentifier=NEWID();
INSERT dbo.NOTIFICATION(notification_id,user_id,source_message_id,type,title,message)
VALUES(@notification,@user,@message,'BookingCreated',N'Test',N'Test');
BEGIN TRY
    INSERT dbo.NOTIFICATION(user_id,source_message_id,type,title,message)
    VALUES(@user,@message,'BookingCreated',N'Test',N'Test');
    THROW 51000, 'Duplicate notification accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
BEGIN TRY
    UPDATE dbo.NOTIFICATION SET data_json=N'bad json' WHERE notification_id=@notification;
    THROW 51000, 'Invalid notification JSON accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
UPDATE dbo.NOTIFICATION SET read_at=SYSUTCDATETIME() WHERE user_id=@user AND read_at IS NULL;
IF @@ROWCOUNT <> 1 THROW 51000, 'Read-all failed', 1;
UPDATE dbo.NOTIFICATION SET read_at=SYSUTCDATETIME() WHERE user_id=@user AND read_at IS NULL;
IF @@ROWCOUNT <> 0 THROW 51000, 'Read-all is not idempotent', 1;
DECLARE @token nvarchar(100)=CONVERT(nvarchar(36),NEWID());
INSERT dbo.DEVICE_TOKEN(user_id,provider,device_id,token) VALUES(@user,'FCM',CONVERT(varchar(36),NEWID()),@token);
BEGIN TRY
    INSERT dbo.DEVICE_TOKEN(user_id,provider,device_id,token) VALUES(NEWID(),'FCM',CONVERT(varchar(36),NEWID()),@token);
    THROW 51000, 'Push token registered twice', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
INSERT dbo.INBOX_MESSAGE(consumer,message_id) VALUES('test',@message);
BEGIN TRY
    INSERT dbo.INBOX_MESSAGE(consumer,message_id) VALUES('test',@message);
    THROW 51000, 'Inbox duplicate accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
ROLLBACK;
PRINT 'PASS notification: source deduplication, JSON, read-all, device tokens, inbox';
