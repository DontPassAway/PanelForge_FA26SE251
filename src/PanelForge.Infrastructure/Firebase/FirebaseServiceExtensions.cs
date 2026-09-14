using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PanelForge.Infrastructure.Firebase;

public static class FirebaseServiceExtensions
{
    public static IServiceCollection AddFirebaseServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var projectId = configuration["Firebase:ProjectId"];
        var credentialsPath = configuration["Firebase:ServiceAccountKeyPath"];

        if (string.IsNullOrWhiteSpace(projectId) || projectId.Equals("YOUR_FIREBASE_PROJECT_ID", StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        if (FirebaseApp.DefaultInstance is not null)
        {
            return services;
        }

        GoogleCredential credential;

        if (!string.IsNullOrWhiteSpace(credentialsPath) && File.Exists(credentialsPath))
        {
#pragma warning disable CS0618
            using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read);
            credential = GoogleCredential.FromStream(stream);
#pragma warning restore CS0618
        }
        else
        {
            try
            {
                credential = GoogleCredential.GetApplicationDefault();
            }
            catch (Exception)
            {
                return services;
            }
        }

        FirebaseApp.Create(new AppOptions
        {
            Credential = credential,
            ProjectId = projectId
        });

        return services;
    }
}
