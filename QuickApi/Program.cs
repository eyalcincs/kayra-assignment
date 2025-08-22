using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using QuickApi.Application.Products; // CQRS tarafındaki MediatR handler'larım bu assembly'de
using QuickApi.Data;                 // EF Core DbContext
using QuickApi.Infrastructure;       // Redis cache servis arayüz ve implementasyonu
using QuickApi.Models;               // DTO'lar / Auth modelleri

using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Veritabanı (EF Core + PostgreSQL)
// appsettings.json içindeki "ConnectionStrings:Default" değerini kullanıyorum.

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

//Swagger + Controller + API Explorer
// Swagger'da "Authorize" butonu ile Bearer/JWT token girebilmek için güvenlik şemasını tanımlıyorum.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "QuickApi", Version = "v1" });

    // Swagger'da üstte çıkan "Authorize" düğmesi için Bearer şeması
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // "Bu API Bearer kullanıyor" bilgisini tüm endpoint'lere yay
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors();

// Kimlik Doğrulama (JWT)
// Issuer/Audience/Key bilgilerini appsettings:Jwt altından alıyorum.
// Token doğrulamada clock skew'i 30 sn yapıyorum.
// =============================================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        var cfg = builder.Configuration;

        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = cfg["Jwt:Issuer"],
            ValidAudience = cfg["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)),

            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// MediatR (CQRS)
// Handler'larım QuickApi.Application.Products assembly'sinde.

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(GetProductsQuery).Assembly));

// Redis Cache
// Docker'da çalışan redis container'a "localhost:6379" üzerinden bağlanıyorum.
// appsettings:ConnectionStrings:Redis ayarı yoksa varsayılanı kullanıyorum.

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var cs = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(cs);
});
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Uygulamayı oluşturuyorum
var app = builder.Build();

// Basit global exception middleware
// Hata durumlarını tek elden düzgün bir JSON formatında döndürüyorum.

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (ArgumentException ex)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex); // Log'lanabilir
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

app.UseAuthentication();
app.UseAuthorization();

// Endpoint Grupları

//   PRODUCTS

var products = app.MapGroup("/api/products").WithTags("Products");

// LIST: sayfalı listeleme
products.MapGet("/", async (
        int page,
        int pageSize,
        string? search,
        string? sort,
        IMediator mediator) =>
{
    page     = page     <= 0 ? 1  : page;
    pageSize = pageSize <= 0 ? 20 : pageSize;

    var dto = new ProductListQueryDto(page, pageSize, search, sort);
    var result = await mediator.Send(new GetProductsQuery(dto));
    return Results.Ok(result);
});

// DETAIL
products.MapGet("/{id:int}", async (int id, AppDbContext db) =>
    await db.Products.FindAsync(id) is { } p ? Results.Ok(p) : Results.NotFound());

// CREATE: ürün ekleme 
products.MapPost("/", async (ProductCreateDto input, IMediator mediator) =>
{
    var id = await mediator.Send(new CreateProductCommand(input));
    return Results.Created($"/api/products/{id}", new { id });
}).RequireAuthorization();

// UPDATE: ürün güncelleme 
products.MapPut("/{id:int}", async (int id, ProductUpdateDto input, IMediator mediator) =>
{
    await mediator.Send(new UpdateProductCommand(id, input));
    return Results.NoContent();
}).RequireAuthorization();

// DELETE: ürün silme 
products.MapDelete("/{id:int}", async (int id, IMediator mediator) =>
{
    await mediator.Send(new DeleteProductCommand(id));
    return Results.NoContent();
}).RequireAuthorization();

//  AUTH

var auth = app.MapGroup("/api/auth").WithTags("Auth");

// REGISTER: e-posta benzersiz olmalı, şifre hash'lenir
auth.MapPost("/register", async (RegisterRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email ve Password zorunlu" });

    var email = req.Email.Trim().ToLowerInvariant();
    var exists = await db.Users.AnyAsync(u => u.Email == email);
    if (exists) return Results.Conflict(new { error = "Email zaten kayıtlı" });

    var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
    var user = new User { Email = email, PasswordHash = hash, FullName = req.FullName };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Email, user.FullName });
});

// LOGIN: doğru şifrede JWT üretir
auth.MapPost("/login", async (LoginRequest req, AppDbContext db, IConfiguration cfg) =>
{
    var email = (req.Email ?? "").Trim().ToLowerInvariant();
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    if (user is null) return Results.Unauthorized();

    var ok = BCrypt.Net.BCrypt.Verify(req.Password ?? "", user.PasswordHash);
    if (!ok) return Results.Unauthorized();

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expires = DateTime.UtcNow.AddMinutes(
        double.TryParse(cfg["Jwt:ExpireMinutes"], out var m) ? m : 60);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(ClaimTypes.Name, user.Email)
    };

    var token = new JwtSecurityToken(
        issuer: cfg["Jwt:Issuer"],
        audience: cfg["Jwt:Audience"],
        claims: claims,
        expires: expires,
        signingCredentials: creds);

    var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new AuthResponse(tokenStr, expires));
});

app.MapGet("/", () => Results.Redirect("/swagger"));

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Migration apply error: {ex}");
}

app.Run();