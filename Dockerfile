# Use the official ASP.NET runtime image as a base
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["InvoiceGen.csproj", "./"]
RUN dotnet restore "./InvoiceGen.csproj"
COPY . .
RUN dotnet publish "InvoiceGen.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InvoiceGen.dll"]
