# example-net
Eample .NET application

## Build executable
### Visual Studio
### Command line

## Pack
### Docker image
### Helm chart
### GitHub actions

## Configuration
### Local configuration
1. Set local environment variable **DOTNET_ENVIRONMENT=local**
When start application from Visual Studion this environment variable is configured in **launchSettings.json** file.

2. Configure settings in **appsettings.local.json**
```json
{
  "Settings": {
    "TimeSpan": 2,
    "ShutdownTimeout": 10,
    "UseApm": true
  },
  "ElasticApm": {
    "ServerUrls": "https://elastic-mp-platform-sandbox.apm.westeurope.azure.elastic-cloud.com",
    "ServiceName": "example-net",
    "Environment": "Development",
    "ApiKey": "xxx==",
    "MetricsInterval": "2m",
    "Enabled": true
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
      },
      {
        "Name": "Debug",
        "Formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
      },
      {
        "Name": "Seq",
        "Args": { "serverUrl": "http://localhost:5341" },
        "Formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
      },
      {
        "Name": "UdpSyslog",
        "Args": {
          "host": "127.0.0.1",
          "port": 514
        }
      },
      {
        "Name": "Elasticsearch",
        "Args": {
          "nodeUris": "https://elastic-mp-platform-sandbox.es.westeurope.azure.elastic-cloud.com",
          "indexFormat": "example-net-index-{0:yyyy}",
          "connectionGlobalHeaders": "Authorization=Basic xxx="
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithElasticApmCorrelationInfo" ]
  }
}
```
### Docker
### Kubernetes

## Run
### Executable
### Docker
1. Pull the Docker image
2. 
```sh
docker run example-net-docker -p 5000:8080 --env DOTNET_ENVIRONMENT=local
``` 
### Docker Compose
### Kubernetes
1. Download the Helm chart

2. Install Helm Chart
If the Helm chart is downloaded locally as a *.tgz file
```sh
helm install example-net example-net-helm-0.1.10.tgz --namespace example-net --create-namespace
``` 

