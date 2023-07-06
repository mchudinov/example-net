FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build-env
WORKDIR /App

COPY . ./
RUN dotnet restore

# Build and publish a release
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:7.0
ENV DOTNET_EnableDiagnostics=0
WORKDIR /App
COPY --from=build-env /App/out .

EXPOSE 50001/tcp

ENTRYPOINT ["dotnet", "ExampleNet.dll"]