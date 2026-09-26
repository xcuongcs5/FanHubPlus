using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FanHub.PaymentWalletService.Data;

namespace FanHub.PaymentWalletService.Providers;

public sealed record Settlement(string Reference, string ProviderId, decimal Amount, bool Succeeded, byte[] Hash);
public sealed record ProviderResult(bool Verified, string Status, string? ProviderId = null);
public interface IVnPayGateway
{
    string Checkout(PaymentTransaction payment, string ip);
    Settlement? Verify(IReadOnlyDictionary<string, string> fields);
    Task<ProviderResult> QueryAsync(PaymentTransaction payment, CancellationToken ct);
    Task<ProviderResult> RefundAsync(PaymentTransaction payment, PaymentRefund refund, CancellationToken ct);
}

public sealed class VnPay(IConfiguration config, IHttpClientFactory clients) : IVnPayGateway
{
    private string Merchant => config["VnPay:TmnCode"]!;
    private string Secret => config["VnPay:HashSecret"]!;
    public static string Date(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    public static string Sign(string secret, string data) => Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));
    public static bool Matches(string expected, string? supplied)
    {
        if (supplied?.Length != expected.Length) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(supplied)); }
        catch (FormatException) { return false; }
    }
    public static string Canonical(IReadOnlyDictionary<string, string> fields) => string.Join("&", fields
        .Where(x => x.Key.StartsWith("vnp_", StringComparison.Ordinal) && x.Key is not ("vnp_SecureHash" or "vnp_SecureHashType") && x.Value.Length > 0)
        .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => WebUtility.UrlEncode(x.Key) + "=" + WebUtility.UrlEncode(x.Value)));
    public string Checkout(PaymentTransaction p, string ip)
    {
        var data = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0", ["vnp_Command"] = "pay", ["vnp_TmnCode"] = Merchant,
            ["vnp_Amount"] = Amount(p.Amount), ["vnp_CreateDate"] = Date(p.CreatedAt), ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = ip, ["vnp_Locale"] = "vn", ["vnp_OrderInfo"] = "Thanh toan FanHub " + p.MerchantReference,
            ["vnp_OrderType"] = "other", ["vnp_ReturnUrl"] = config["VnPay:ReturnUrl"]!,
            ["vnp_ExpireDate"] = Date(p.ExpiresAt), ["vnp_TxnRef"] = p.MerchantReference
        };
        var canonical = Canonical(data);
        return config["VnPay:PaymentUrl"] + "?" + canonical + "&vnp_SecureHash=" + Sign(Secret, canonical);
    }
    public Settlement? Verify(IReadOnlyDictionary<string, string> f)
    {
        if (!config.GetValue("VnPay:Enabled", false) || f.GetValueOrDefault("vnp_TmnCode") != Merchant ||
            !Matches(Sign(Secret, Canonical(f)), f.GetValueOrDefault("vnp_SecureHash"))) return null;
        var reference = f.GetValueOrDefault("vnp_TxnRef", "");
        var id = f.GetValueOrDefault("vnp_TransactionNo", "");
        if (reference.Length is < 1 or > 100 || id.Length is < 1 or > 15 || !id.All(char.IsAsciiDigit) ||
            !long.TryParse(f.GetValueOrDefault("vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount % 100 != 0 ||
            f.GetValueOrDefault("vnp_CurrCode", "VND") != "VND" ||
            f.GetValueOrDefault("vnp_ResponseCode")?.Length != 2 || f.GetValueOrDefault("vnp_TransactionStatus")?.Length != 2) return null;
        var success = f["vnp_ResponseCode"] == "00" && f["vnp_TransactionStatus"] == "00";
        if (success && id == "0") return null;
        return new(reference, id, amount / 100m, success, SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(f))));
    }
    public static string Amount(decimal amount) => checked((long)(amount * 100)).ToString(CultureInfo.InvariantCulture);
    public async Task<ProviderResult> QueryAsync(PaymentTransaction p, CancellationToken ct)
    {
        var d = Base(p, "querydr", Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        d["vnp_OrderInfo"] = "Query " + p.MerchantReference;
        d["vnp_SecureHash"] = Sign(Secret, Join(d, "vnp_RequestId|vnp_Version|vnp_Command|vnp_TmnCode|vnp_TxnRef|vnp_TransactionDate|vnp_CreateDate|vnp_IpAddr|vnp_OrderInfo"));
        return await SendAsync(d, p, p.Amount, "01", true, ct);
    }
    public async Task<ProviderResult> RefundAsync(PaymentTransaction p, PaymentRefund refund, CancellationToken ct)
    {
        var d = Base(p, "refund", refund.RefundId.ToString("N"), refund.AttemptedAt!.Value);
        d["vnp_TransactionType"] = "02"; // Full refund: Booking's result contract is full-settlement only.
        d["vnp_Amount"] = Amount(refund.Amount);
        d["vnp_TransactionNo"] = p.ProviderTransactionId!;
        d["vnp_CreateBy"] = refund.RequestedBy.ToString("N");
        d["vnp_OrderInfo"] = "Refund " + refund.RefundId.ToString("N");
        d["vnp_SecureHash"] = Sign(Secret, Join(d, "vnp_RequestId|vnp_Version|vnp_Command|vnp_TmnCode|vnp_TransactionType|vnp_TxnRef|vnp_Amount|vnp_TransactionNo|vnp_TransactionDate|vnp_CreateBy|vnp_CreateDate|vnp_IpAddr|vnp_OrderInfo"));
        return await SendAsync(d, p, refund.Amount, "02", false, ct);
    }
    private Dictionary<string, string> Base(PaymentTransaction p, string command, string id, DateTime date) => new()
    {
        ["vnp_RequestId"] = id, ["vnp_Version"] = "2.1.0", ["vnp_Command"] = command, ["vnp_TmnCode"] = Merchant,
        ["vnp_TxnRef"] = p.MerchantReference, ["vnp_TransactionDate"] = Date(p.CreatedAt),
        ["vnp_CreateDate"] = Date(date), ["vnp_IpAddr"] = config["VnPay:ServerIp"] ?? "127.0.0.1"
    };
    private static string Join(IReadOnlyDictionary<string, string> d, string keys) => string.Join("|", keys.Split('|').Select(k => d.GetValueOrDefault(k, "")));
    private async Task<ProviderResult> SendAsync(Dictionary<string, string> request, PaymentTransaction p, decimal amount, string type, bool query, CancellationToken ct)
    {
        using var response = await clients.CreateClient("provider").PostAsJsonAsync(config["VnPay:ApiUrl"], request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in json.RootElement.EnumerateObject())
            if (!d.TryAdd(property.Name, property.Value.ToString())) return new(false, "Unknown");
        var keys = "vnp_ResponseId|vnp_Command|vnp_ResponseCode|vnp_Message|vnp_TmnCode|vnp_TxnRef|vnp_Amount|vnp_BankCode|vnp_PayDate|vnp_TransactionNo|vnp_TransactionType|vnp_TransactionStatus|vnp_OrderInfo";
        if (query) keys += "|vnp_PromotionCode|vnp_PromotionAmount";
        if (!Matches(Sign(Secret, Join(d, keys)), d.GetValueOrDefault("vnp_SecureHash")) ||
            d.GetValueOrDefault("vnp_TmnCode") != Merchant || d.GetValueOrDefault("vnp_TxnRef") != p.MerchantReference ||
            d.GetValueOrDefault("vnp_Command") != request["vnp_Command"] || d.GetValueOrDefault("vnp_Amount") != Amount(amount)) return new(false, "Unknown");
        // querydr can report a full refund instead of the original payment. Preserve its type.
        var responseType = d.GetValueOrDefault("vnp_TransactionType");
        if (responseType != type && !(query && responseType == "02")) return new(false, "Unknown");
        var status = d.GetValueOrDefault("vnp_TransactionStatus");
        if (d.GetValueOrDefault("vnp_ResponseCode") != "00") return new(true, "Unknown");
        var id = d.GetValueOrDefault("vnp_TransactionNo");
        if (string.IsNullOrEmpty(id) || id.Length > 15 || !id.All(char.IsAsciiDigit) || id == "0") return new(false, "Unknown");
        return new(true, status == "00" ? (query && responseType == "02" ? "Refunded" : "Succeeded") :
            status is "02" or "09" ? "Failed" : "Processing", id);
    }
}
