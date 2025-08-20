using MediatR;
using Microsoft.EntityFrameworkCore;
using QuickApi.Data;
using QuickApi.Infrastructure;

namespace QuickApi.Application.Products;

public record GetProductsQuery(ProductListQueryDto Input) : IRequest<PagedResult<ProductListItemDto>>;

public class GetProductsHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductListItemDto>>
{
    private readonly AppDbContext _db;
    private readonly ICacheService _cache;

    public GetProductsHandler(AppDbContext db, ICacheService cache)
    {
        _db = db; _cache = cache;
    }

    public async Task<PagedResult<ProductListItemDto>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var (page, pageSize, search, sort) = request.Input;
        var key = $"products:list:p{page}:s{pageSize}:q{search}:o{sort}";

        // Cache 
        var cached = await _cache.GetAsync<PagedResult<ProductListItemDto>>(key, ct);
        if (cached is not null) return cached;

        // DB'den getir
        var query = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) || p.Type.ToLower().Contains(s));
        }
        // arrangement
        query = sort switch
        {
            "price_asc"  => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            _            => query.OrderByDescending(p => p.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProductListItemDto(p.Id, p.Name, p.Type, p.Price, p.Quantity, p.CreatedUtc))
            .ToListAsync(ct);

        var result = new PagedResult<ProductListItemDto>(items, total, page, pageSize);

        //Cache’e koy
        await _cache.SetAsync(key, result, TimeSpan.FromSeconds(60), ct);

        return result;
    }
}
