using Microsoft.EntityFrameworkCore;
using QuickApi.Data;
using QuickApi.Models;

var builder = WebApplication.CreateBuilder(args);

// EF Core + PostgreSQL
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();

var app = builder.Build();

// Basit global hata yakalama
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ArgumentException ex)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = "Internal Server Error" });
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

// Minimal API ile �r�n CRUD
app.MapGet("/api/products", async (AppDbContext db) =>
    await db.Products.OrderByDescending(p => p.Id).ToListAsync());

app.MapGet("/api/products/{id:int}", async (int id, AppDbContext db) =>
    await db.Products.FindAsync(id) is { } p ? Results.Ok(p) : Results.NotFound());

app.MapPost("/api/products", async (Product input, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Name zorunlu");
    if (string.IsNullOrWhiteSpace(input.Type)) throw new ArgumentException("Type zorunlu");
    if (input.Price < 0) throw new ArgumentException("Price negatif olamaz");
    if (input.Quantity < 0) throw new ArgumentException("Quantity negatif olamaz");

    db.Products.Add(input);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{input.Id}", input);
});

app.MapPut("/api/products/{id:int}", async (int id, Product update, AppDbContext db) =>
{
    var p = await db.Products.FindAsync(id);
    if (p is null) return Results.NotFound();

    if (string.IsNullOrWhiteSpace(update.Name)) throw new ArgumentException("Name zorunlu");
    if (string.IsNullOrWhiteSpace(update.Type)) throw new ArgumentException("Type zorunlu");
    if (update.Price < 0) throw new ArgumentException("Price negatif olamaz");
    if (update.Quantity < 0) throw new ArgumentException("Quantity negatif olamaz");

    p.Name = update.Name;
    p.Type = update.Type;
    p.Price = update.Price;
    p.Quantity = update.Quantity;
    p.IsActive = update.IsActive;

    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapDelete("/api/products/{id:int}", async (int id, AppDbContext db) =>
{
    var p = await db.Products.FindAsync(id);
    if (p is null) return Results.NotFound();
    db.Remove(p);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();
