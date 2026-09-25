using System.IO;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PanelForge.Application;
using PanelForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Application layer (MediatR + Command/Query handlers) ─────────────────────
builder.Services.AddApplication();

// ── Infrastructure (EF Core + Marten + Auth + Repositories) ─────────────────
builder.Services.AddInfrastructure(builder.Configuration);

var jwtSecret = builder.Configuration["JwtSettings:Secret"]
    ?? throw new InvalidOperationException("JwtSettings:Secret is not configured.");
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"];
var jwtAudience = builder.Configuration["JwtSettings:Audience"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    var signingKeys = new List<SecurityKey>();
    if (!string.IsNullOrEmpty(jwtSecret))
    {
        signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)));
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = !string.IsNullOrEmpty(jwtIssuer),
        ValidIssuer = jwtIssuer,
        ValidateAudience = !string.IsNullOrEmpty(jwtAudience),
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // ... (Code cấu hình Swagger giữ nguyên)
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "PanelForge API",
        Version     = "v1",
        Description = "AI-Assisted Manga Production Management Platform"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "Bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "Enter JWT Bearer token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// [THÊM MỚI] 1. Cấu hình chính sách CORS cấp quyền cho Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173") // Cho phép cả React và Vite
              .AllowAnyHeader()                     // Cho phép gửi mọi Header (như Authorization chứa JWT)
              .AllowAnyMethod()                     // Cho phép mọi method (GET, POST, PUT, DELETE, OPTIONS...)
              .AllowCredentials();                  // Cần thiết nếu dùng Cookie hoặc xác thực đặc thù
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PanelForge API v1");
    options.RoutePrefix = "swagger";
});

//app.UseHttpsRedirection();

// [THÊM MỚI] 2. Kích hoạt middleware CORS (Phải đặt TRƯỚC UseAuthentication)
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();