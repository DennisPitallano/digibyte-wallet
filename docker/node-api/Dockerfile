# DigiByte Node API — Multi-stage .NET 10 build

# === Build stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY DigiByte.slnx .
COPY src/DigiByte.NodeApi/DigiByte.NodeApi.csproj src/DigiByte.NodeApi/
COPY src/DigiByte.Wallet/DigiByte.Wallet.csproj src/DigiByte.Wallet/
COPY src/DigiByte.Crypto/DigiByte.Crypto.csproj src/DigiByte.Crypto/
COPY src/DigiByte.P2P.Shared/DigiByte.P2P.Shared.csproj src/DigiByte.P2P.Shared/
COPY src/DigiByte.Api/DigiByte.Api.csproj src/DigiByte.Api/
COPY src/DigiByte.Web/DigiByte.Web.csproj src/DigiByte.Web/
COPY tests/DigiByte.Crypto.Tests/DigiByte.Crypto.Tests.csproj tests/DigiByte.Crypto.Tests/
COPY tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj tests/DigiByte.Wallet.Tests/
COPY tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj tests/DigiByte.Api.Tests/
COPY tests/DigiByte.NodeApi.Tests/DigiByte.NodeApi.Tests.csproj tests/DigiByte.NodeApi.Tests/

# Restore only NodeApi and its dependencies
RUN dotnet restore src/DigiByte.NodeApi/DigiByte.NodeApi.csproj

# Copy everything and build
COPY . .
RUN dotnet publish src/DigiByte.NodeApi/DigiByte.NodeApi.csproj \
    -c Release -o /app/publish --no-restore

# === Runtime stage ===
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# API port
EXPOSE 5260

# Environment defaults — overridden by docker-compose
ENV ASPNETCORE_URLS=http://+:5260
ENV DigiByteNode__Host=digibyted
ENV DigiByteNode__TestnetPort=14023
ENV DigiByteNode__RpcUser=dgbrpc
ENV DigiByteNode__RpcPassword=changeme
ENV DigiByteNode__IsTestnet=true
ENV DigiByteNode__FaucetEnabled=true

ENTRYPOINT ["dotnet", "DigiByte.NodeApi.dll"]
