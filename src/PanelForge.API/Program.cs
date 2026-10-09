using PanelForge.API.Authentication;
using System.IO;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PanelForge.Application;
using PanelForge.Infrastructure;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Infrastructure.Persistence.Seeders;
using PanelForge.API.Common;

var builder = WebApplication.CreateBuilder(args);

// ── Công cụ: chép ảnh từ đĩa lên R2 rồi thoát (không khởi động web, không chạm DB) ──
//   dotnet run -- migrate-assets-to-r2 [--dry-run] [--source <thư mục>]
if (args.Length > 0 && args[0] == "migrate-assets-to-r2")
{
    var sourceIndex = Array.IndexOf(args, "--source");
    var source = sourceIndex >= 0 && sourceIndex + 1 < args.Length
        ? args[sourceIndex + 1]
        : builder.Configuration["AssetStorage:RootPath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "App_Data", "assets");
    var dryRun = args.Contains("--dry-run");

    if (!Directory.Exists(source))
    {
        Console.Error.WriteLine($"Không thấy thư mục ảnh: {source}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"Nguồn: {Path.GetFullPath(source)}{(dryRun ? "  (chạy thử, không chép)" : "")}");
    var summary = await PanelForge.Infrastructure.Services.AssetStorageMigrator.RunAsync(
        source, dryRun ? null : PanelForge.Infrastructure.Services.R2AssetStorage.FromConfiguration(builder.Configuration),
        Console.Out);
    Console.WriteLine($"Xong: {summary.Copied} {(dryRun ? "sẽ chép" : "đã chép")}, {summary.Invalid} sai hash, {summary.Failed} lỗi.");
    Environment.ExitCode = summary.Invalid + summary.Failed == 0 ? 0 : 1;
    return;
}

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

    // User bị khoá / xoá mềm mất quyền ngay, không phải đợi JWT hết hạn.
    options.Events = new JwtBearerEvents
    {
        // Không có header Bearer → đọc JWT từ cookie HttpOnly pf_access.
        // Chống CSRF: request đổi dữ liệu dùng cookie phải có header X-Requested-With (trang khác không tự gắn được).
        OnMessageReceived = context =>
        {
            var request = context.HttpContext.Request;
            if (string.IsNullOrEmpty(context.Token)
                && !request.Headers.ContainsKey("Authorization")
                && request.Cookies.TryGetValue(AuthCookies.Access, out var cookieToken)
                && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
                    || AuthCookies.HasCsrfHeader(request)))
            {
                context.Token = cookieToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var sub = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                      ?? context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var db = context.HttpContext.RequestServices.GetRequiredService<IPanelForgeDbContext>();
            if (!Guid.TryParse(sub, out var userId)
                || !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, context.HttpContext.RequestAborted))
                context.Fail("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");
        }
    };
});

builder.Services.AddAuthorization();

// ── AI Multi-Agent: đánh dấu run dở (AI service chết/restart) là Interrupted ──────────
builder.Services.AddHostedService<AiRunSweeper>();

// ── UC-14: nhắc người nhận việc sắp đến hạn / quá hạn ─────────────────────────────────
builder.Services.AddHostedService<AssignmentReminderWorker>();

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

// Chống dò mật khẩu / OTP: tối đa 10 request/phút/IP cho các endpoint auth ([EnableRateLimiting("auth")]).
// ponytail: giới hạn theo IP, chưa theo email; khoá tài khoản sau N lần sai OTP cần thêm cột + migration.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

// ── Migrate & Seed master data on startup ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PanelForgeDbContext>();
    await db.Database.MigrateAsync();
    await MasterDataSeeder.SeedAsync(db);

    if (app.Environment.IsDevelopment())
        await DevUserSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>());

    // Dữ liệu mẫu demo AI: opt-in (AiService:SeedDemoData=true), chạy lặp không tạo trùng, không sửa dữ liệu có sẵn
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("AiService:SeedDemoData"))
    {
        var seeded = await AiDemoSeeder.SeedAsync(db);
        app.Logger.LogInformation("AiDemoSeeder: {Result}", seeded ? "đã seed Studio Moonlight" : "bỏ qua (đã có hoặc thiếu tài khoản dev)");
    }
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PanelForge API v1");
    options.RoutePrefix = "swagger";
});

//app.UseHttpsRedirection();

// [THÊM MỚI] 2. Kích hoạt middleware CORS (Phải đặt TRƯỚC UseAuthentication)
app.UseCors("AllowFrontend");

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();