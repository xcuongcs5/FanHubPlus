using System.Net;
using System.Text;
using System.Text.Json;
using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Providers;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FanHub.PaymentWalletService.IntegrationTests;

public sealed class VnPayProtocolTests
{
    [Fact]
    public void Hmac_matches_RFC4231_and_canonical_encoding_is_deterministic()
    {
        Assert.Equal("164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737",
            VnPay.Sign("Jefe", "what do ya want for nothing?"));
        Assert.Equal("vnp_A=a+b%2B%2F%25&vnp_B=2", VnPay.Canonical(new Dictionary<string, string>
            { ["vnp_B"] = "2", ["vnp_A"] = "a b+/%", ["vnp_Empty"] = "", ["vnp_SecureHash"] = "ignored", ["not_vnp"] = "ignored" }));
        Assert.Equal("20260101065959", VnPay.Date(new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
    }
    [Theory]
    [InlineData("valid", true, "Succeeded")]
    [InlineData("signature", false, "Unknown")]
    [InlineData("merchant", false, "Unknown")]
    [InlineData("amount", false, "Unknown")]
    [InlineData("reference", false, "Unknown")]
    [InlineData("pending", true, "Processing")]
    public async Task Refund_response_requires_authentic_matching_settlement(string variant, bool verified, string status)
    {
        var p = new PaymentTransaction { MerchantReference = "merchant-reference", Amount = 50000, ProviderTransactionId = "12345",
            CreatedAt = new DateTime(2026, 1, 2, 1, 2, 3, DateTimeKind.Utc) };
        var refund = new PaymentRefund { Amount = p.Amount, RequestedBy = Guid.NewGuid(), AttemptedAt = DateTime.UtcNow };
        Dictionary<string, string>? request = null;
        using var factory = new FakeClients(async body =>
        {
            request = JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;
            var response = new Dictionary<string, string>
            {
                ["vnp_ResponseId"] = "response-1", ["vnp_Command"] = "refund", ["vnp_ResponseCode"] = "00", ["vnp_Message"] = "OK",
                ["vnp_TmnCode"] = variant == "merchant" ? "OTHERONE" : "TESTCODE", ["vnp_TxnRef"] = variant == "reference" ? "other" : p.MerchantReference,
                ["vnp_Amount"] = variant == "amount" ? "1" : "5000000", ["vnp_BankCode"] = "NCB", ["vnp_PayDate"] = "20260102100203",
                ["vnp_TransactionNo"] = "999999", ["vnp_TransactionType"] = "02", ["vnp_TransactionStatus"] = variant == "pending" ? "05" : "00",
                ["vnp_OrderInfo"] = "Refund"
            };
            var signing = string.Join("|", new[] { "vnp_ResponseId", "vnp_Command", "vnp_ResponseCode", "vnp_Message", "vnp_TmnCode", "vnp_TxnRef", "vnp_Amount", "vnp_BankCode", "vnp_PayDate", "vnp_TransactionNo", "vnp_TransactionType", "vnp_TransactionStatus", "vnp_OrderInfo" }.Select(k => response[k]));
            response["vnp_SecureHash"] = variant == "signature" ? new string('0', 128) : VnPay.Sign(PaymentFixture.ProviderSecret, signing);
            await Task.CompletedTask;
            return JsonSerializer.Serialize(response);
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VnPay:TmnCode"] = "TESTCODE", ["VnPay:HashSecret"] = PaymentFixture.ProviderSecret,
            ["VnPay:ApiUrl"] = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction", ["VnPay:ServerIp"] = "127.0.0.1"
        }).Build();
        var result = await new VnPay(config, factory).RefundAsync(p, refund, default);
        Assert.Equal(verified, result.Verified); Assert.Equal(status, result.Status);
        Assert.Equal(refund.RefundId.ToString("N"), request!["vnp_RequestId"]);
        Assert.Equal("20260102080203", request["vnp_TransactionDate"]);
        Assert.Equal("5000000", request["vnp_Amount"]);
    }
    private sealed class FakeClients(Func<string, Task<string>> reply) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => new(HttpStatusCode.OK)
        { Content = new StringContent(await reply(await request.Content!.ReadAsStringAsync(ct)), Encoding.UTF8, "application/json") };
    }
}
