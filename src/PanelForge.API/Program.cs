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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);


var firebaseConfig = builder.Configuration.GetSection("Firebase");
var projectId = firebaseConfig["ProjectId"] ?? throw new InvalidOperationException("Firebase:ProjectId is missing in configuration.");
var keyPath = firebaseConfig["ServiceAccountKeyPath"];


var jwtSecret = builder.Configuration["JwtSettings:Secret"];
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"];
var jwtAudience = builder.Configuration["JwtSettings:Audience"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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
        IssuerSigningKeys = signingKeys,
        ValidateIssuer = !string.IsNullOrEmpty(jwtIssuer),
        ValidIssuer = jwtIssuer,
        ValidateAudience = !string.IsNullOrEmpty(jwtAudience),
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
    };

    if (!string.IsNullOrWhiteSpace(projectId) && !projectId.Equals("YOUR_FIREBASE_PROJECT_ID", StringComparison.OrdinalIgnoreCase))
    {
        options.Authority = $"https://securetoken.google.com/{projectId}";
    }
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(PanelForge.Domain.Enums.SystemRole.Admin.ToString()));
    options.AddPolicy("StaffOnly", policy => policy.RequireRole(
        PanelForge.Domain.Enums.SystemRole.Admin.ToString(),
        PanelForge.Domain.Enums.SystemRole.Moderator.ToString()));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PanelForge API",
        Version = "v1",
        Description = "AI-Assisted Manga Production Management Platform"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
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

builder.Services.AddControllers();


if (!string.IsNullOrEmpty(keyPath))
{
    var fullKeyPath = Path.Combine(builder.Environment.ContentRootPath, keyPath);
    if (File.Exists(fullKeyPath))
    {
        if (FirebaseApp.DefaultInstance == null)
        {
            using (var stream = new FileStream(fullKeyPath, FileMode.Open, FileAccess.Read))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromStream(stream),
                    ProjectId = projectId
                });
            }
            Console.WriteLine("--> Firebase đã được khởi tạo thành công!");
        }
        else
        {
            Console.WriteLine("--> Firebase đã tồn tại, tái sử dụng instance cũ.");
        }
    }
    else
    {
        Console.WriteLine($"--> [CẢNH BÁO] Không tìm thấy file Firebase key tại: {fullKeyPath}");
    }
}

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PanelForge API v1");
    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();