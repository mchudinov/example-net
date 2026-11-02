FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /App

COPY . ./
# Restore and publish only the app project: the test project must never reach the image
RUN dotnet restore ExampleNet/ExampleNet.csproj

# Build and publish a release
RUN dotnet publish ExampleNet/ExampleNet.csproj -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /App
EXPOSE 50001/tcp
ENV DOTNET_EnableDiagnostics=0
COPY --from=build-env /App/out .
ENTRYPOINT ["dotnet", "ExampleNet.dll"]