using System.IO;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PanelForge.Application;
using PanelForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);


var firebaseConfig = builder.Configuration.GetSection("Firebase");
var projectId = firebaseConfig["ProjectId"] ?? throw new InvalidOperationException("Firebase:ProjectId is missing in configuration.");
var keyPath = firebaseConfig["ServiceAccountKeyPath"];


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{

    options.Authority = $"https://securetoken.google.com/{projectId}";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = $"https://securetoken.google.com/{projectId}",
        ValidateAudience = true,
        ValidAudience = projectId,
        ValidateLifetime = true
    };
});


builder.Services.AddAuthorization();

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
        Description = "Enter Firebase JWT Bearer token"
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
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