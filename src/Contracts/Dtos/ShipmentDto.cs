namespace Contracts.Dtos
{
    public record ShipmentDto
    {
        public Guid ShipmentId { get; set; }
        public int OrderId { get; set; } = default!;
        public string Address { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
    }
}