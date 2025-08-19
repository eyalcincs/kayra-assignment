using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QuickApi.Models;
using QuickApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models; // ADDED

var builder = WebApplication.CreateBuilder(args);

// EF Core + PostgreSQL (appsettings.json'daki "Default" bağlantısını okur)
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ADDED: Swagger'a Bearer/JWT desteği
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "QuickApi", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme
            { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
          Array.Empty<string>() }
    });
});

builder.Services.AddCors();

// JWT Authentication
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

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

// ADDED: Auth middleware'leri (endpoint'lerden ÖNCE olmalı)
app.UseAuthentication();   // ADDED
app.UseAuthorization();    // ADDED

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

// Minimal API ile Product CRUD
var products = app.MapGroup("/api/products").WithTags("Products"); // ADDED (etiket)

// GET'ler herkese açık
products.MapGet("/", async (AppDbContext db) =>
    await db.Products.OrderByDescending(p => p.Id).ToListAsync());

products.MapGet("/{id:int}", async (int id, AppDbContext db) =>
    await db.Products.FindAsync(id) is { } p ? Results.Ok(p) : Results.NotFound());
// 👉 BUNU EKLE (MapGet'in HEMEN ALTINA)
var auth = app.MapGroup("/api/auth").WithTags("Auth");

// REGISTER
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
}).WithOpenApi(); // (İsteğe bağlı ama Swagger listesinde işe yarar)

// LOGIN
auth.MapPost("/login", async (LoginRequest req, AppDbContext db, IConfiguration cfg) =>
{
    var email = (req.Email ?? "").Trim().ToLowerInvariant();
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    if (user is null) return Results.Unauthorized();

    var ok = BCrypt.Net.BCrypt.Verify(req.Password ?? "", user.PasswordHash);
    if (!ok) return Results.Unauthorized();

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expires = DateTime.UtcNow.AddMinutes(double.TryParse(cfg["Jwt:ExpireMinutes"], out var m) ? m : 60);

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
        signingCredentials: creds
    );

    var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new AuthResponse(tokenStr, expires));
}).WithOpenApi();

// Yazma işlemleri JWT ister
products.MapPost("/", async (Product input, AppDbContext db) =>
{
    input.Id = 0;
    input.CreatedUtc = DateTime.UtcNow;

    if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Name zorunlu");
    if (string.IsNullOrWhiteSpace(input.Type)) throw new ArgumentException("Type zorunlu");
    if (input.Price < 0) throw new ArgumentException("Price negatif olamaz");
    if (input.Quantity < 0) throw new ArgumentException("Quantity negatif olamaz");

    db.Products.Add(input);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{input.Id}", input);
}).RequireAuthorization();

products.MapPut("/{id:int}", async (int id, Product update, AppDbContext db) =>
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
}).RequireAuthorization();

products.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
{
    var p = await db.Products.FindAsync(id);
    if (p is null) return Results.NotFound();
    db.Remove(p);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/_debug/routes", (Microsoft.AspNetCore.Routing.EndpointDataSource es) =>
    Results.Json(es.Endpoints
        .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
        .Select(e => e.RoutePattern.RawText)
        .OrderBy(p => p)));

app.Run();
