namespace QuickApi.Application.Products;

public record ProductCreateDto(string Name, string Type, decimal Price, int Quantity);
public record ProductUpdateDto(string Name, string Type, decimal Price, int Quantity);
public record ProductListItemDto(int Id, string Name, string Type, decimal Price, int Quantity, DateTime CreatedUtc);


public record ProductListQueryDto(int Page = 1, int PageSize = 20, string? Search = null, string? Sort = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
