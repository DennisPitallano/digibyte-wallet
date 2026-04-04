# DigiByte API — Multi-stage .NET 10 build

# === Build stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY DigiByte.slnx .
COPY src/DigiByte.Api/DigiByte.Api.csproj src/DigiByte.Api/
COPY src/DigiByte.Wallet/DigiByte.Wallet.csproj src/DigiByte.Wallet/
COPY src/DigiByte.Crypto/DigiByte.Crypto.csproj src/DigiByte.Crypto/
COPY src/DigiByte.P2P.Shared/DigiByte.P2P.Shared.csproj src/DigiByte.P2P.Shared/
COPY src/DigiByte.NodeApi/DigiByte.NodeApi.csproj src/DigiByte.NodeApi/
COPY src/DigiByte.Web/DigiByte.Web.csproj src/DigiByte.Web/
COPY tests/DigiByte.Crypto.Tests/DigiByte.Crypto.Tests.csproj tests/DigiByte.Crypto.Tests/
COPY tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj tests/DigiByte.Wallet.Tests/
COPY tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj tests/DigiByte.Api.Tests/
COPY tests/DigiByte.NodeApi.Tests/DigiByte.NodeApi.Tests.csproj tests/DigiByte.NodeApi.Tests/

# Restore only Api and its dependencies
RUN dotnet restore src/DigiByte.Api/DigiByte.Api.csproj

# Copy everything and build
COPY . .
RUN dotnet publish src/DigiByte.Api/DigiByte.Api.csproj \
    -c Release -o /app/publish --no-restore

# === Runtime stage ===
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "DigiByte.Api.dll"]
