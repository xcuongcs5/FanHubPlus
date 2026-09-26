using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Providers;
using FanHub.PaymentWalletService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FanHub.PaymentWalletService.Api;

[ApiController, Route("api/v1"), Authorize, EnableRateLimiting("user")]
[RequestSizeLimit(16384)]
public sealed class PaymentsController(PaymentDbContext db, PaymentService service, IVnPayGateway gateway, IConfiguration config) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ClientIp => HttpContext.Connection.RemoteIpAddress is { } ip ?
        (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString()) : "127.0.0.1";
    [HttpPost("payments/create-intent")]
    public async Task<IActionResult> Intent(IntentRequest request, CancellationToken ct) => Ok(PaymentService.View(
        await service.CreateAsync(UserId, request, null, ClientIp, ct)));
    [HttpPost("wallets/deposit")]
    public async Task<IActionResult> Deposit(DepositRequest request, CancellationToken ct) => Ok(PaymentService.View(
        await service.CreateAsync(UserId, null, request, ClientIp, ct)));
    [HttpGet("payments/{bookingId:guid}/status")]
    public async Task<IActionResult> Status(Guid bookingId, CancellationToken ct)
    {
        var payments = await db.Transactions.AsNoTracking().Where(x => x.BookingId == bookingId && x.UserId == UserId)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        if (payments.Count == 0) throw PaymentService.Missing();
        var ids = payments.Select(x => x.TransactionId).ToArray();
        var refunds = await db.Refunds.AsNoTracking().Where(x => ids.Contains(x.TransactionId)).ToListAsync(ct);
        return Ok(new { payments = payments.Select(PaymentService.View), refunds = refunds.Select(PaymentService.View) });
    }
    [HttpPost("payments/refund")]
    public async Task<IActionResult> Refund(RefundRequest request, CancellationToken ct) => Accepted(PaymentService.View(await service.RefundAsync(UserId, request, ct)));
    [HttpGet("wallets/me")]
    public async Task<IActionResult> Wallet(CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == UserId && x.Currency == "VND", ct);
        return Ok(new { wallet_id = wallet?.WalletId, currency = "VND", balance = wallet?.Balance ?? 0m, status = wallet?.Status ?? "NotCreated" });
    }
    [HttpGet("wallets/transactions")]
    public async Task<IActionResult> Transactions([FromQuery] PageQuery paging, CancellationToken ct)
    {
        var query = db.Ledgers.AsNoTracking().Where(x => x.Wallet.UserId == UserId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.LedgerId)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize)
            .Select(x => new { x.LedgerId, x.TransactionId, x.RefundId, x.EntryType, x.Amount, x.BalanceAfter, x.CreatedAt }).ToListAsync(ct);
        return Ok(new { items, total, paging.Page, paging.PageSize });
    }
    // VNPAY's actual IPN is GET. POST form/query compatibility also preserves assigned task 44.
    [AllowAnonymous, DisableRateLimiting, HttpGet("payments/webhook/vnpay"), HttpPost("payments/webhook/vnpay"), RequestSizeLimit(16384)]
    public async Task<IActionResult> VnPayIpn(CancellationToken ct)
    {
        var fields = await VnPayFields(ct);
        var result = fields is null ? null : gateway.Verify(fields);
        var code = result is null ? "97" : await service.SettleAsync("VNPay", result, ct);
        return new JsonResult(new Dictionary<string, string> { ["RspCode"] = code, ["Message"] = code switch
            { "00" => "Confirm Success", "02" => "Order already confirmed", "01" => "Order not found", "04" => "Invalid amount", _ => "Invalid request" } });
    }
    [AllowAnonymous, DisableRateLimiting, HttpGet("payments/return/vnpay")]
    public async Task<IActionResult> Return(CancellationToken ct)
    {
        var fields = await VnPayFields(ct);
        var result = fields is null ? null : gateway.Verify(fields);
        if (result is null) return BadRequest(new { code = "invalid_signature" });
        // This page is informational only; never mutate balances or publish a settlement from browser navigation.
        return Ok(new { signature_valid = true, provider_reported_success = result.Succeeded,
            message = "Payment confirmation is processed separately by IPN. Check authenticated payment status." });
    }
    private async Task<Dictionary<string, string>?> VnPayFields(CancellationToken ct)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in Request.Query)
            if (field.Value.Count != 1 || field.Value.ToString().Length > 2048 || !data.TryAdd(field.Key, field.Value.ToString())) return null;
        if (Request.Method == "POST" && Request.HasFormContentType)
            foreach (var field in await Request.ReadFormAsync(ct))
                if (field.Value.Count != 1 || field.Value.ToString().Length > 2048 || !data.TryAdd(field.Key, field.Value.ToString())) return null;
        return data.Count > 40 ? null : data;
    }
    [AllowAnonymous, DisableRateLimiting, HttpPost("payments/webhook/momo"), RequestSizeLimit(16384)]
    public async Task<IActionResult> MoMo([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!config.GetValue("MoMo:Enabled", false)) return StatusCode(503, new { code = "provider_not_configured" });
        if (body.ValueKind != JsonValueKind.Object) return BadRequest();
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
            if (!d.TryAdd(property.Name, property.Value.ToString()) || property.Value.ToString().Length > 2048) return BadRequest();
        var keys = "amount,extraData,message,orderId,orderInfo,orderType,partnerCode,payType,requestId,responseTime,resultCode,transId".Split(',');
        if (keys.Any(k => !d.ContainsKey(k)) || d["partnerCode"] != config["MoMo:PartnerCode"] || d["orderId"] != d["requestId"])
            return BadRequest();
        var raw = "accessKey=" + config["MoMo:AccessKey"] + "&" + string.Join("&", keys.Select(k => k + "=" + d[k]));
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config["MoMo:SecretKey"]!), Encoding.UTF8.GetBytes(raw)));
        if (!VnPay.Matches(expected, d.GetValueOrDefault("signature"))) return Unauthorized();
        if (!long.TryParse(d["amount"], out var amount) || amount <= 0 || !long.TryParse(d["transId"], out var id) ||
            !int.TryParse(d["resultCode"], out var resultCode) || d["orderId"].Length > 100 || (resultCode == 0 && id <= 0)) return BadRequest();
        // 9000 is authorization only, not capture. Processing responses cannot settle a payment.
        if (resultCode is 9000 or 1000 or 7000 or 7002) return NoContent();
        var code = await service.SettleAsync("MoMo", new Settlement(d["orderId"], d["transId"], amount, resultCode == 0,
            SHA256.HashData(Encoding.UTF8.GetBytes(raw))), ct);
        return code is "00" or "02" ? NoContent() : BadRequest(new { code });
    }
}
