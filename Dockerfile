FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /App

# Restore layer: rebuilt only when global.json or the project file changes.
# Only the app project is copied: the test project must never reach the image.
# Root-level build files (global.json; any future Directory.Build.props,
# Directory.Packages.props or NuGet.config) must be copied here explicitly.
COPY global.json ./
COPY src/ExampleNet/ExampleNet.csproj src/ExampleNet/
RUN dotnet restore src/ExampleNet/ExampleNet.csproj

# Source layer: build and publish a release
COPY src/ExampleNet/ src/ExampleNet/
RUN dotnet publish src/ExampleNet/ExampleNet.csproj -c Release --no-restore -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /App
EXPOSE 50001/tcp
ENV DOTNET_EnableDiagnostics=0
COPY --from=build-env /App/out .
ENTRYPOINT ["dotnet", "ExampleNet.dll"]