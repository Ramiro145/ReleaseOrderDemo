namespace Contracts.Dtos
{
    public class ProductDto
    {
        public int ProductId { get; set; } = default!;
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string Description { get; set; } = default!;
        public int Stock { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; } = true;
    }
}