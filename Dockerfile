#FROM mcr.microsoft.com/dotnet/aspnet:9.0@sha256:b4bea3a52a0a77317fa93c5bbdb076623f81e3e2f201078d89914da71318b5d8
# --- DEVELOPMENT STAGE ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
WORKDIR /src
# VS Code Dev Containers will use this stage and mount your code here

# --- BUILD STAGE ---
FROM dev AS build
COPY . .
RUN dotnet publish "eventplatform.csproj" --configuration Release --os linux --self-contained false -o /app/publish

# --- RUNTIME STAGE (Production) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "eventplatform.dll"]