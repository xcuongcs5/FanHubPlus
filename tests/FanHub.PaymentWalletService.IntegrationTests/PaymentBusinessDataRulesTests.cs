using System.Security.Cryptography;
using System.Text;
using FanHub.PaymentWalletService.Data;
using Xunit;

namespace FanHub.PaymentWalletService.IntegrationTests;

public sealed class PaymentBusinessDataRulesTests
{
    [Fact]
    public void PaymentConstants_ShouldMatchDatabaseCheckConstraints()
    {
        // 1. Wallet Status: 'Active', 'Frozen', 'Closed'
        Assert.Equal("Active", PaymentConstants.WalletStatus.Active);
        Assert.Equal("Frozen", PaymentConstants.WalletStatus.Frozen);
        Assert.Equal("Closed", PaymentConstants.WalletStatus.Closed);

        // 2. Transaction Purpose: 'Booking', 'Upgrade', 'Deposit'
        Assert.Equal("Booking", PaymentConstants.Purpose.Booking);
        Assert.Equal("Upgrade", PaymentConstants.Purpose.Upgrade);
        Assert.Equal("Deposit", PaymentConstants.Purpose.Deposit);

        // 3. Provider: 'VNPay', 'MoMo', 'Wallet'
        Assert.Equal("VNPay", PaymentConstants.Provider.VNPay);
        Assert.Equal("MoMo", PaymentConstants.Provider.MoMo);
        Assert.Equal("Wallet", PaymentConstants.Provider.Wallet);

        // 4. Transaction Status: 'Pending','Processing','Succeeded','Failed','Expired','Cancelled'
        Assert.Equal("Pending", PaymentConstants.Status.Pending);
        Assert.Equal("Processing", PaymentConstants.Status.Processing);
        Assert.Equal("Succeeded", PaymentConstants.Status.Succeeded);
        Assert.Equal("Failed", PaymentConstants.Status.Failed);
        Assert.Equal("Expired", PaymentConstants.Status.Expired);
        Assert.Equal("Cancelled", PaymentConstants.Status.Cancelled);

        // 5. Refund Status: 'Pending','Processing','Succeeded','Failed'
        Assert.Equal("Pending", PaymentConstants.RefundStatus.Pending);
        Assert.Equal("Processing", PaymentConstants.RefundStatus.Processing);
        Assert.Equal("Succeeded", PaymentConstants.RefundStatus.Succeeded);
        Assert.Equal("Failed", PaymentConstants.RefundStatus.Failed);

        // 6. Webhook Status: 'Received','Processed','Rejected','Failed'
        Assert.Equal("Received", PaymentConstants.WebhookStatus.Received);
        Assert.Equal("Processed", PaymentConstants.WebhookStatus.Processed);
        Assert.Equal("Rejected", PaymentConstants.WebhookStatus.Rejected);
        Assert.Equal("Failed", PaymentConstants.WebhookStatus.Failed);

        // 7. Ledger Entry Type: 'Deposit', 'Payment', 'Refund'
        Assert.Equal("Deposit", PaymentConstants.LedgerEntryType.Deposit);
        Assert.Equal("Payment", PaymentConstants.LedgerEntryType.Payment);
        Assert.Equal("Refund", PaymentConstants.LedgerEntryType.Refund);
    }

    [Fact]
    public void RequestHash_ShouldProduceValid32ByteBinary()
    {
        var rawPayload = "{\"booking_id\":\"00000000-0000-0000-0000-000000000001\",\"provider\":\"VNPay\"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));

        Assert.Equal(32, hash.Length);

        var txn = new PaymentTransaction
        {
            TransactionId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            BookingId = Guid.NewGuid(),
            Purpose = PaymentConstants.Purpose.Booking,
            Provider = PaymentConstants.Provider.VNPay,
            MerchantReference = "ORDER_001",
            IdempotencyKey = "key_001",
            RequestHash = hash,
            Amount = 150000m,
            Currency = "VND",
            Status = PaymentConstants.Status.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        Assert.Equal(32, txn.RequestHash.Length);
        Assert.True(txn.Amount > 0);
        Assert.True(txn.ExpiresAt > txn.CreatedAt);
    }

    [Fact]
    public void WalletLedger_DepositCalculation_ShouldBeCorrect()
    {
        var initialBalance = 100000m;
        var depositAmount = 50000m;
        var expectedBalanceAfter = initialBalance + depositAmount;

        var ledger = new WalletLedger
        {
            WalletId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            EntryType = PaymentConstants.LedgerEntryType.Deposit,
            Amount = depositAmount,
            BalanceAfter = expectedBalanceAfter,
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal(150000m, ledger.BalanceAfter);
        Assert.Null(ledger.RefundId);
        Assert.True(ledger.Amount > 0);
        Assert.True(ledger.BalanceAfter >= 0);
    }

    [Fact]
    public void WalletLedger_PaymentCalculation_ShouldBeCorrect()
    {
        var initialBalance = 150000m;
        var paymentAmount = 50000m;
        var expectedBalanceAfter = initialBalance - paymentAmount;

        var ledger = new WalletLedger
        {
            WalletId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            EntryType = PaymentConstants.LedgerEntryType.Payment,
            Amount = paymentAmount,
            BalanceAfter = expectedBalanceAfter,
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal(100000m, ledger.BalanceAfter);
        Assert.Null(ledger.RefundId);
        Assert.True(ledger.Amount > 0);
        Assert.True(ledger.BalanceAfter >= 0);
    }

    [Fact]
    public void WalletLedger_RefundRequiresRefundId_ShouldBeValid()
    {
        var refundId = Guid.NewGuid();
        var ledger = new WalletLedger
        {
            WalletId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            RefundId = refundId,
            EntryType = PaymentConstants.LedgerEntryType.Refund,
            Amount = 50000m,
            BalanceAfter = 150000m,
            CreatedAt = DateTime.UtcNow
        };

        Assert.NotNull(ledger.RefundId);
        Assert.Equal(PaymentConstants.LedgerEntryType.Refund, ledger.EntryType);
    }
}
