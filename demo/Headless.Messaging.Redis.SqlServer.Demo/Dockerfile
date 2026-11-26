# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:ca396ece3f2a2e62c7a03f1d7d710b8bc7e8c9dea5597d091178d9f5bfca13f0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80

# Supplies Node and npm only: the demo references the messaging dashboard, whose build runs npm ci and a Vite build
# of its SPA, and the .NET SDK image ships no Node.
FROM node:22-bookworm-slim@sha256:be979e5523c76fa4d5ca4ebd3099078f220aa47ec39c48ad5b5df9314debd6eb AS node

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/npm
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && node --version && npm --version
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# The SDK version, the target framework, and every package version live in the repository-wide build files, and the
# demo restores a closure of framework projects, so the whole tree is copied before the restore.
COPY . .
RUN dotnet restore "./demo/Headless.Messaging.Redis.SqlServer.Demo/Headless.Messaging.Redis.SqlServer.Demo.csproj"
WORKDIR "/src/demo/Headless.Messaging.Redis.SqlServer.Demo"
RUN dotnet build "./Headless.Messaging.Redis.SqlServer.Demo.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Headless.Messaging.Redis.SqlServer.Demo.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Headless.Messaging.Redis.SqlServer.Demo.dll"]
