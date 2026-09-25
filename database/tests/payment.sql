SET NOCOUNT ON;
SET XACT_ABORT OFF;
BEGIN TRANSACTION;
DECLARE @user uniqueidentifier=NEWID(), @wallet uniqueidentifier=NEWID(), @payment uniqueidentifier=NEWID(),
    @booking uniqueidentifier=NEWID(), @hash binary(32)=HASHBYTES('SHA2_256','test');
INSERT dbo.WALLET(wallet_id,user_id) VALUES(@wallet,@user);
BEGIN TRY
    UPDATE dbo.WALLET SET balance=-1 WHERE wallet_id=@wallet;
    THROW 51000, 'Negative wallet balance accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
INSERT dbo.BOOKING_PROJECTION(booking_id,user_id,event_id,amount,currency,status,expires_at)
VALUES(@booking,@user,NEWID(),100000,'VND','Reserved',DATEADD(minute,15,SYSUTCDATETIME()));
INSERT dbo.PAYMENT_TRANSACTION(transaction_id,user_id,booking_id,purpose,provider,merchant_reference,idempotency_key,request_hash,amount,status,expires_at)
VALUES(@payment,@user,@booking,'Booking','VNPay',CONVERT(varchar(36),NEWID()),'first',@hash,100000,'Succeeded',DATEADD(minute,15,SYSUTCDATETIME()));
BEGIN TRY
    INSERT dbo.PAYMENT_TRANSACTION(user_id,booking_id,purpose,provider,merchant_reference,idempotency_key,request_hash,amount,status,expires_at)
    VALUES(@user,@booking,'Booking','MoMo',CONVERT(varchar(36),NEWID()),'second',@hash,100000,'Succeeded',DATEADD(minute,15,SYSUTCDATETIME()));
    THROW 51000, 'Booking paid twice', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
INSERT dbo.PAYMENT_WEBHOOK(provider,provider_event_key,transaction_id,payload_hash)
VALUES('VNPay',CONVERT(varchar(36),@payment),@payment,@hash);
BEGIN TRY
    INSERT dbo.PAYMENT_WEBHOOK(provider,provider_event_key,transaction_id,payload_hash)
    VALUES('VNPay',CONVERT(varchar(36),@payment),@payment,@hash);
    THROW 51000, 'Duplicate webhook accepted', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
DECLARE @deposit uniqueidentifier=NEWID();
INSERT dbo.PAYMENT_TRANSACTION(transaction_id,user_id,wallet_id,purpose,provider,merchant_reference,idempotency_key,request_hash,amount,status,expires_at)
VALUES(@deposit,@user,@wallet,'Deposit','MoMo',CONVERT(varchar(36),NEWID()),'deposit',@hash,50000,'Succeeded',DATEADD(minute,15,SYSUTCDATETIME()));
UPDATE dbo.WALLET SET balance=50000 WHERE wallet_id=@wallet;
INSERT dbo.WALLET_LEDGER(wallet_id,transaction_id,entry_type,amount,balance_after)
VALUES(@wallet,@deposit,'Deposit',50000,50000);
BEGIN TRY
    INSERT dbo.WALLET_LEDGER(wallet_id,transaction_id,entry_type,amount,balance_after)
    VALUES(@wallet,@deposit,'Deposit',50000,100000);
    THROW 51000, 'Deposit credited twice', 1;
END TRY BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
ROLLBACK;
PRINT 'PASS payment: nonnegative wallet, single successful payment, webhook and ledger deduplication';
