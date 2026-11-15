# DigiByte SPV gateway — Multi-stage .NET 10 build

# === Build stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first (for layer caching). The gateway depends on DigiByte.Crypto only.
COPY src/DigiByte.SpvGateway/DigiByte.SpvGateway.csproj src/DigiByte.SpvGateway/
COPY src/DigiByte.Crypto/DigiByte.Crypto.csproj src/DigiByte.Crypto/
COPY Directory.Build.props .

RUN dotnet restore src/DigiByte.SpvGateway/DigiByte.SpvGateway.csproj

COPY src/DigiByte.SpvGateway/ src/DigiByte.SpvGateway/
COPY src/DigiByte.Crypto/ src/DigiByte.Crypto/
RUN dotnet publish src/DigiByte.SpvGateway/DigiByte.SpvGateway.csproj \
    -c Release -o /app/publish --no-restore

# === Runtime stage ===
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# API port
EXPOSE 5290

# SQLite files (headers, block index, peer book) live in /data. No VOLUME instruction: Railway rejects it ("use Railway Volumes"), so mount a
# Railway Volume at /data (docs/RAILWAY_NODE.md). Without one the data is lost on each deploy and the gateway syncs from scratch.

# Environment defaults, overridden by docker-compose / Railway. Mainnet by default.
ENV ASPNETCORE_URLS=http://+:5290
ENV SpvGateway__Network=mainnet
ENV SpvGateway__DataDirectory=/data

ENTRYPOINT ["dotnet", "DigiByte.SpvGateway.dll"]
