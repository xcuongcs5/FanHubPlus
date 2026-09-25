using FanHub.PaymentWalletService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FanHub.PaymentWalletService.IntegrationTests;

public sealed class PaymentDatabaseModelTests
{
    private readonly PaymentDbContext _dbContext;

    public PaymentDatabaseModelTests()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer("Server=localhost,14334;Database=fanhub_payment;User Id=sa;Password=DummyPassword123!;Encrypt=True;TrustServerCertificate=True")
            .Options;
        _dbContext = new PaymentDbContext(options);
    }

    [Fact]
    public void Model_ShouldCompileWithoutErrors()
    {
        var model = _dbContext.Model;
        Assert.NotNull(model);
    }

    [Theory]
    [InlineData(typeof(Wallet), "WALLET")]
    [InlineData(typeof(PaymentTransaction), "PAYMENT_TRANSACTION")]
    [InlineData(typeof(WalletLedger), "WALLET_LEDGER")]
    [InlineData(typeof(PaymentRefund), "PAYMENT_REFUND")]
    [InlineData(typeof(PaymentWebhook), "PAYMENT_WEBHOOK")]
    [InlineData(typeof(BookingProjection), "BOOKING_PROJECTION")]
    [InlineData(typeof(UpgradeProjection), "UPGRADE_PROJECTION")]
    [InlineData(typeof(UserProjection), "USER_PROJECTION")]
    [InlineData(typeof(OutboxMessage), "OUTBOX_MESSAGE")]
    [InlineData(typeof(InboxMessage), "INBOX_MESSAGE")]
    public void Entity_ShouldMapToCorrectTableName(Type entityType, string expectedTableName)
    {
        var entity = _dbContext.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        Assert.Equal(expectedTableName, entity.GetTableName());
    }

    [Fact]
    public void ConcurrencyTokens_ShouldBeProperlyConfigured()
    {
        var wallet = _dbContext.Model.FindEntityType(typeof(Wallet))!;
        var versionProp = wallet.FindProperty(nameof(Wallet.Version))!;
        Assert.True(versionProp.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, versionProp.ValueGenerated);

        var txn = _dbContext.Model.FindEntityType(typeof(PaymentTransaction))!;
        var txnVersion = txn.FindProperty(nameof(PaymentTransaction.Version))!;
        Assert.True(txnVersion.IsConcurrencyToken);

        var refund = _dbContext.Model.FindEntityType(typeof(PaymentRefund))!;
        var refundVersion = refund.FindProperty(nameof(PaymentRefund.Version))!;
        Assert.True(refundVersion.IsConcurrencyToken);
    }

    [Fact]
    public void DecimalPrecision_ShouldBe19_4_ForMonetaryColumns()
    {
        var wallet = _dbContext.Model.FindEntityType(typeof(Wallet))!;
        var balance = wallet.FindProperty(nameof(Wallet.Balance))!;
        Assert.Equal(19, balance.GetPrecision());
        Assert.Equal(4, balance.GetScale());
        Assert.Equal("decimal(19,4)", balance.GetColumnType());

        var txn = _dbContext.Model.FindEntityType(typeof(PaymentTransaction))!;
        var amount = txn.FindProperty(nameof(PaymentTransaction.Amount))!;
        Assert.Equal(19, amount.GetPrecision());
        Assert.Equal(4, amount.GetScale());
        Assert.Equal("decimal(19,4)", amount.GetColumnType());

        var ledger = _dbContext.Model.FindEntityType(typeof(WalletLedger))!;
        var ledgerAmount = ledger.FindProperty(nameof(WalletLedger.Amount))!;
        Assert.Equal("decimal(19,4)", ledgerAmount.GetColumnType());
        var balanceAfter = ledger.FindProperty(nameof(WalletLedger.BalanceAfter))!;
        Assert.Equal("decimal(19,4)", balanceAfter.GetColumnType());

        var refund = _dbContext.Model.FindEntityType(typeof(PaymentRefund))!;
        var refundAmount = refund.FindProperty(nameof(PaymentRefund.Amount))!;
        Assert.Equal("decimal(19,4)", refundAmount.GetColumnType());
    }

    [Fact]
    public void BinaryColumns_ShouldBeBinary32()
    {
        var txn = _dbContext.Model.FindEntityType(typeof(PaymentTransaction))!;
        var reqHash = txn.FindProperty(nameof(PaymentTransaction.RequestHash))!;
        Assert.Equal("binary(32)", reqHash.GetColumnType());

        var webhook = _dbContext.Model.FindEntityType(typeof(PaymentWebhook))!;
        var payloadHash = webhook.FindProperty(nameof(PaymentWebhook.PayloadHash))!;
        Assert.Equal("binary(32)", payloadHash.GetColumnType());
    }

    [Fact]
    public void UniqueConstraintsAndFilteredIndexes_ShouldBeConfigured()
    {
        // 1. UQ_WALLET_user_currency
        var wallet = _dbContext.Model.FindEntityType(typeof(Wallet))!;
        var walletIdx = wallet.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UQ_WALLET_user_currency");
        Assert.NotNull(walletIdx);
        Assert.True(walletIdx.IsUnique);

        // 2. PAYMENT_TRANSACTION indexes
        var txn = _dbContext.Model.FindEntityType(typeof(PaymentTransaction))!;
        var refIdx = txn.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UQ_PAYMENT_TRANSACTION_reference");
        Assert.NotNull(refIdx);
        Assert.True(refIdx.IsUnique);

        var idempIdx = txn.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UQ_PAYMENT_TRANSACTION_idempotency");
        Assert.NotNull(idempIdx);
        Assert.True(idempIdx.IsUnique);

        var providerTxnIdx = txn.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UX_PAYMENT_provider_transaction");
        Assert.NotNull(providerTxnIdx);
        Assert.True(providerTxnIdx.IsUnique);
        Assert.Contains("provider_transaction_id", providerTxnIdx.GetFilter() ?? "");

        var bookingSuccessIdx = txn.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UX_PAYMENT_booking_success");
        Assert.NotNull(bookingSuccessIdx);
        Assert.True(bookingSuccessIdx.IsUnique);
        Assert.Contains("Succeeded", bookingSuccessIdx.GetFilter() ?? "");

        var upgradeSuccessIdx = txn.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UX_PAYMENT_upgrade_success");
        Assert.NotNull(upgradeSuccessIdx);
        Assert.True(upgradeSuccessIdx.IsUnique);
        Assert.Contains("Succeeded", upgradeSuccessIdx.GetFilter() ?? "");

        // 3. PAYMENT_WEBHOOK
        var webhook = _dbContext.Model.FindEntityType(typeof(PaymentWebhook))!;
        var webhookIdx = webhook.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UQ_PAYMENT_WEBHOOK_event");
        Assert.NotNull(webhookIdx);
        Assert.True(webhookIdx.IsUnique);

        // 4. PAYMENT_REFUND
        var refund = _dbContext.Model.FindEntityType(typeof(PaymentRefund))!;
        var refundIdx = refund.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UQ_PAYMENT_REFUND_request");
        Assert.NotNull(refundIdx);
        Assert.True(refundIdx.IsUnique);

        // 5. WALLET_LEDGER
        var ledger = _dbContext.Model.FindEntityType(typeof(WalletLedger))!;
        var ledgerPayIdx = ledger.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UX_WALLET_LEDGER_payment");
        Assert.NotNull(ledgerPayIdx);
        Assert.True(ledgerPayIdx.IsUnique);
        Assert.Contains("NULL", ledgerPayIdx.GetFilter() ?? "");

        var ledgerRefundIdx = ledger.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UX_WALLET_LEDGER_refund");
        Assert.NotNull(ledgerRefundIdx);
        Assert.True(ledgerRefundIdx.IsUnique);
        Assert.Contains("NOT NULL", ledgerRefundIdx.GetFilter() ?? "");
    }

    [Fact]
    public void ForeignKeys_ShouldHaveRestrictDeleteBehavior()
    {
        var txn = _dbContext.Model.FindEntityType(typeof(PaymentTransaction))!;
        foreach (var fk in txn.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        var ledger = _dbContext.Model.FindEntityType(typeof(WalletLedger))!;
        foreach (var fk in ledger.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        var refund = _dbContext.Model.FindEntityType(typeof(PaymentRefund))!;
        foreach (var fk in refund.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        var webhook = _dbContext.Model.FindEntityType(typeof(PaymentWebhook))!;
        foreach (var fk in webhook.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void CompositeKeysAndForeignKeys_ShouldBeProperlyConfigured()
    {
        // InboxMessage composite PK
        var inbox = _dbContext.Model.FindEntityType(typeof(InboxMessage))!;
        var inboxPk = inbox.FindPrimaryKey();
        Assert.NotNull(inboxPk);
        Assert.Equal(2, inboxPk.Properties.Count);
        Assert.Equal("consumer", inboxPk.Properties[0].GetColumnName());
        Assert.Equal("message_id", inboxPk.Properties[1].GetColumnName());

        // UpgradeProjection composite Unique
        var upgrade = _dbContext.Model.FindEntityType(typeof(UpgradeProjection))!;
        var upgradeUnique = upgrade.GetIndexes().FirstOrDefault(x => x.GetDatabaseName() == "UQ_UPGRADE_PROJECTION_booking");
        Assert.NotNull(upgradeUnique);
        Assert.True(upgradeUnique.IsUnique);
        Assert.Equal(2, upgradeUnique.Properties.Count);

        // PaymentTransaction FK to UpgradeProjection (composite)
        var txn = _dbContext.Model.FindEntityType(typeof(PaymentTransaction))!;
        var upgradeFk = txn.GetForeignKeys().FirstOrDefault(x => x.PrincipalEntityType == upgrade);
        Assert.NotNull(upgradeFk);
        Assert.Equal(2, upgradeFk.Properties.Count);
    }
}
