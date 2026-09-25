using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace FanHub.PaymentWalletService.Api;

public sealed record IntentRequest(Guid BookingId, Guid? UpgradeId,
    [Required, RegularExpression("^(VNPay|Wallet|MoMo)$")] string Provider,
    [Required, RegularExpression("^[A-Za-z0-9._:-]{1,100}$")] string IdempotencyKey);
public sealed record DepositRequest([Range(typeof(decimal), "10000", "100000000")] decimal Amount,
    [Required, RegularExpression("^(VNPay|MoMo)$")] string Provider,
    [Required, RegularExpression("^[A-Za-z0-9._:-]{1,100}$")] string IdempotencyKey);
public sealed record RefundRequest(Guid TransactionId,
    [Required, RegularExpression("^[A-Za-z0-9._:-]{1,100}$")] string IdempotencyKey,
    [Required, StringLength(500, MinimumLength = 3)] string Reason);
public sealed class PageQuery
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [FromQuery(Name = "page_size"), Range(1, 100)] public int PageSize { get; set; } = 20;
}
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
