namespace Contracts.Dtos
{
    public record PaymentDto
    (
        Guid PaymentId,
        int OrderId,
        decimal Amount,
        string Status,
        DateTime CreatedAt
    );
}