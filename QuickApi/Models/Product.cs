using System.Text.Json.Serialization;

namespace QuickApi.Models;

public class Product
{
    public int Id { get; set; }

    
    [JsonPropertyName("Product name")]
    public required string Name { get; set; }

    
    [JsonPropertyName("Product type")]
    public required string Type { get; set; }

    
    [JsonPropertyName("Product price")]
    public decimal Price { get; set; }

   
    [JsonPropertyName("Product quantity")]
    public int Quantity { get; set; } = 0;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

