using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.Models;
using StockPortfolio.Api.Options;
using StockPortfolio.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// JWT Configuration
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName)
);

var jwt = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");

if (string.IsNullOrWhiteSpace(jwt.Key) || Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
}

// PostgreSQL / Supabase Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// Services
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IExcelImportService, ExcelImportService>();

builder.Services.AddHttpClient("OneDrive");
builder.Services.AddScoped<IOneDriveExcelService, OneDriveExcelService>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<HoldingsPriceService>();

builder.Services.AddHttpClient<IMarketDataService, YahooFinanceMarketDataService>(
    client =>
    {
        client.BaseAddress = new Uri("https://query1.finance.yahoo.com");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 StockPortfolio/1.0"
        );
        client.Timeout = TimeSpan.FromSeconds(20);
    }
);

builder.Services.AddHttpClient<NseBhavcopyService>(
    client =>
    {
        client.BaseAddress = new Uri("https://nsearchives.nseindia.com");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 StockPortfolio/1.0"
        );
        client.Timeout = TimeSpan.FromSeconds(30);
    }
);

// Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.Key)
            ),

            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "Stock Portfolio API",
            Version = "v1"
        }
    );

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header
        }
    );

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            [
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }
            ] = Array.Empty<string>()
        }
    );
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "Frontend",
        policy =>
        {
            policy
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithOrigins(
                    "http://localhost:5173",
                    "https://localhost:5173",
                    "http://localhost:5174",
                    "https://localhost:5174"
                );
        }
    );
});

var app = builder.Build();

// Swagger enabled for both Development and Production
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint(
        "/swagger/v1/swagger.json",
        "Stock Portfolio API v1"
    );

    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();