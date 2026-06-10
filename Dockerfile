# Use the official .NET Core SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY PollApi.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

# Expose the API port
EXPOSE 5298

# Set environment variables
ENV ASPNETCORE_URLS=http://*:5298
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "PollApi.dll"]
