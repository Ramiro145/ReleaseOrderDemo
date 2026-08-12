namespace Contracts.Dtos
{
    public record OrderDto
    {
        public int OrderId { get; set; } = default!;
        public string OrderCode { get; set; } = default!;
        public int ProductId { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Dirección de envío incluida en la entidad
        public string Address { get; set; } = default!;
    }

    public record CreateOrderRequest
    {
        public string OrderCode { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public int ProductId { get; set; } = default!;
        public decimal Amount { get; set; } = default!;
        public string Address { get; set; } = string.Empty;
    }

    public record OrderReportResult
    {
        public int OrderId { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime GeneratedAt { get; set; }
        public string Summary { get; set; } = default!;
    }

    public record OrderReport
    {
        public int OrderId { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string WorkflowResult { get; set; } = default!;
    }
}