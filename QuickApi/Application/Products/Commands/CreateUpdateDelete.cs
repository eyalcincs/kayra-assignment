using MediatR;
using Microsoft.EntityFrameworkCore;
using QuickApi.Data;
using QuickApi.Infrastructure;
using QuickApi.Models;

namespace QuickApi.Application.Products;

// CREATE
public record CreateProductCommand(ProductCreateDto Input) : IRequest<int>;
public class CreateProductHandler : IRequestHandler<CreateProductCommand, int>
{
    private readonly AppDbContext _db; private readonly ICacheService _cache;
    public CreateProductHandler(AppDbContext db, ICacheService cache) { _db = db; _cache = cache; }

    public async Task<int> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var x = request.Input;
        if (string.IsNullOrWhiteSpace(x.Name)) throw new ArgumentException("Name zorunlu");
        if (string.IsNullOrWhiteSpace(x.Type)) throw new ArgumentException("Type zorunlu");
        if (x.Price < 0 || x.Quantity < 0) throw new ArgumentException("Negatif değer olamaz");

        var entity = new Product { Name = x.Name.Trim(), Type = x.Type.Trim(), Price = x.Price, Quantity = x.Quantity, CreatedUtc = DateTime.UtcNow, IsActive = true};
        _db.Products.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Tüm list cache’lerini patlat
        await _cache.RemoveByPrefixAsync("products:list:", ct);
        return entity.Id;
    }
}
// UPDATE
public record UpdateProductCommand(int Id, ProductUpdateDto Input) : IRequest<Unit>;

public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, Unit>
{
    private readonly AppDbContext _db;
    private readonly ICacheService _cache;

    public UpdateProductHandler(AppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(UpdateProductCommand request, CancellationToken ct)
    {
        var p = await _db.Products.FindAsync(new object?[] { request.Id }, ct);
        if (p is null) throw new KeyNotFoundException("Product not found");

        var x = request.Input;
        if (string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Type))
            throw new ArgumentException("Name/Type zorunlu");
        if (x.Price < 0 || x.Quantity < 0)
            throw new ArgumentException("Negatif değer olamaz");

        p.Name = x.Name.Trim();
        p.Type = x.Type.Trim();
        p.Price = x.Price;
        p.Quantity = x.Quantity;

        await _db.SaveChangesAsync(ct);

        await _cache.RemoveByPrefixAsync("products:list:", ct);
        return Unit.Value;
    }
}
// DELETE
public record DeleteProductCommand(int Id) : IRequest<Unit>;

public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, Unit>
{
    private readonly AppDbContext _db;
    private readonly ICacheService _cache;

    public DeleteProductHandler(AppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteProductCommand request, CancellationToken ct)
    {
        var p = await _db.Products.FindAsync(new object?[] { request.Id }, ct);
        if (p is null) return Unit.Value;

        _db.Remove(p);
        await _db.SaveChangesAsync(ct);

        await _cache.RemoveByPrefixAsync("products:list:", ct);
        return Unit.Value;
    }
}
