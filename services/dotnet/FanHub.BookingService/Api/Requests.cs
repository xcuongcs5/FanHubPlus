using System.ComponentModel.DataAnnotations;

namespace FanHub.BookingService.Api;

public sealed record ReserveRequest(Guid EventId, Guid TicketTypeId, [Range(1, 100)] int Quantity,
    [Required, RegularExpression("^[A-Za-z0-9._:-]{1,100}$")] string IdempotencyKey);
public sealed record TransferRequest(Guid BookingId, Guid ToUserId,
    [Required, RegularExpression("^[A-Za-z0-9._:-]{1,100}$")] string IdempotencyKey);
public sealed record UpgradeRequest(Guid TicketTypeId,
    [Required, RegularExpression("^[A-Za-z0-9._:-]{1,100}$")] string IdempotencyKey);
public sealed record ValidateRequest(Guid BookingId, [Required, StringLength(200)] string NftTokenId,
    Guid UserId, long Timestamp, [Required, RegularExpression("^0x[0-9a-fA-F]{130}$")] string BlockchainSignature);
public sealed class PageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
