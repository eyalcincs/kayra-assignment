using System.Text.Json.Serialization;

namespace QuickApi.Models;

public class Product
{
    public int Id { get; set; }

    // JSON'da "Product name" olarak görünsün
    [JsonPropertyName("Product name")]
    public required string Name { get; set; }

    // JSON'da "Product type"
    [JsonPropertyName("Product type")]
    public required string Type { get; set; }

    // JSON'da "Product price"
    [JsonPropertyName("Product price")]
    public decimal Price { get; set; }

    // JSON'da "Product quantity"
    [JsonPropertyName("Product quantity")]
    public int Quantity { get; set; } = 0;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

