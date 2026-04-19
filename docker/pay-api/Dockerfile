# DigiByte.Pay.Api — non-custodial DigiByte payment gateway API.
# Multi-stage .NET 10 build following the same shape as docker/api/Dockerfile.

# === Build stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files up front so `dotnet restore` can be cached independently
# of source changes. Pay.Api depends on DigiByte.Crypto + DigiByte.Wallet
# (it reuses NBitcoin derivation + blockchain services); those are pulled
# in transitively via the solution.
COPY DigiByte.slnx .
COPY src/DigiByte.Pay.Api/DigiByte.Pay.Api.csproj src/DigiByte.Pay.Api/
COPY src/DigiByte.Pay.Web/DigiByte.Pay.Web.csproj src/DigiByte.Pay.Web/
COPY src/DigiByte.Wallet/DigiByte.Wallet.csproj src/DigiByte.Wallet/
COPY src/DigiByte.Crypto/DigiByte.Crypto.csproj src/DigiByte.Crypto/
COPY src/DigiByte.P2P.Shared/DigiByte.P2P.Shared.csproj src/DigiByte.P2P.Shared/
COPY src/DigiByte.Api/DigiByte.Api.csproj src/DigiByte.Api/
COPY src/DigiByte.NodeApi/DigiByte.NodeApi.csproj src/DigiByte.NodeApi/
COPY src/DigiByte.Web/DigiByte.Web.csproj src/DigiByte.Web/
COPY tests/DigiByte.Crypto.Tests/DigiByte.Crypto.Tests.csproj tests/DigiByte.Crypto.Tests/
COPY tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj tests/DigiByte.Wallet.Tests/
COPY tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj tests/DigiByte.Api.Tests/
COPY tests/DigiByte.NodeApi.Tests/DigiByte.NodeApi.Tests.csproj tests/DigiByte.NodeApi.Tests/

RUN dotnet restore src/DigiByte.Pay.Api/DigiByte.Pay.Api.csproj

# Now bring in the full source and publish.
COPY . .
RUN dotnet publish src/DigiByte.Pay.Api/DigiByte.Pay.Api.csproj \
    -c Release -o /app/publish --no-restore

# === Runtime stage ===
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Railway injects PORT at runtime; Program.cs already binds to it. Fallback
# of 8080 so `docker run` without PORT still works.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "DigiByte.Pay.Api.dll"]
