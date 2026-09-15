# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy các file .csproj để tận dụng Docker layer caching
COPY ["src/PanelForge.API/PanelForge.API.csproj", "src/PanelForge.API/"]
COPY ["src/PanelForge.Application/PanelForge.Application.csproj", "src/PanelForge.Application/"]
COPY ["src/PanelForge.Domain/PanelForge.Domain.csproj", "src/PanelForge.Domain/"]
COPY ["src/PanelForge.Infrastructure/PanelForge.Infrastructure.csproj", "src/PanelForge.Infrastructure/"]

RUN dotnet restore "src/PanelForge.API/PanelForge.API.csproj"

# Copy toàn bộ mã nguồn và build
COPY . .
WORKDIR "/app/src/PanelForge.API"
RUN dotnet publish "PanelForge.API.csproj" -c Release -o /out /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PanelForge.API.dll"]