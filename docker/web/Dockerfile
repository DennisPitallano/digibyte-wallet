# DigiByte Web — Multi-stage .NET 10 Blazor WASM + Nginx

# === Build stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install Node.js for Tailwind CSS build
RUN apt-get update && apt-get install -y --no-install-recommends nodejs npm && rm -rf /var/lib/apt/lists/*

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

# Restore Web and its dependencies
RUN dotnet restore src/DigiByte.Web/DigiByte.Web.csproj

# Copy everything
COPY . .

# Install npm dependencies for Tailwind CSS
WORKDIR /src/src/DigiByte.Web
RUN npm ci

# Publish Blazor WASM (includes Tailwind build via MSBuild target)
WORKDIR /src
RUN dotnet publish src/DigiByte.Web/DigiByte.Web.csproj \
    -c Release -o /app/publish --no-restore

# === Runtime stage — Nginx to serve static files ===
FROM nginx:alpine AS runtime

# Remove default nginx site
RUN rm -rf /usr/share/nginx/html/*

# Copy published Blazor WASM output
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

# Nginx config for Blazor WASM SPA routing
COPY docker/web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
