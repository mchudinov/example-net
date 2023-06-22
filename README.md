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
The following logging options are supported:
- Console
- Debug window in Visual Studio
- Seq
- Syslog TCP/UDP
- Elasticsearch
 
Elastic APM module can be switched on and off with *ElasticApm:Enabled* option.

### Local configuration
1. Set local environment variable **DOTNET_ENVIRONMENT=local**
When start application from Visual Studion this environment variable is configured in **launchSettings.json** file.

2. Configure settings in **appsettings.local.json**
```json
{
  "Settings": {
    "TimeSpan": 2,
    "ShutdownTimeout": 10
  },
  "ElasticApm": {
    "ServerUrls": "https://elastic-mp-platform-sandbox.apm.westeurope.azure.elastic-cloud.com",
    "ServiceName": "example-net",
    "Environment": "Development/SOL/VFY/PROD",
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
Local container configuration
1. Set local environment variable **DOTNET_ENVIRONMENT=localcontainer**
When start application from Visual Studion this environment variable is configured in **launchSettings.json** file.

2. Configure settings in **appsettings.localcontainer.json**
```json
{
  "Settings": {
    "TimeSpan": 2,
    "ShutdownTimeout": 10
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
        "Name": "Elasticsearch",
        "Args": {
          "nodeUris": "https://elastic-mp-platform-sandbox.es.westeurope.azure.elastic-cloud.com",
          "indexFormat": "example-net-index-{0:yyyy}",
          "connectionGlobalHeaders": "Authorization=Basic xxx="
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithElasticApmCorrelationInfo" ]
  },
  "Kestrel": {
    "EndPoints": {
      "Http": {
        "Url": "http://localhost:50001"
      }
    }
  }
}
```
### Kubernetes

## Run
### Executable
### Docker
1. Pull the Docker image
2. Run container
```sh
docker run --publish 50001:8080 --env DOTNET_ENVIRONMENT=localcontainer example-net-docker
``` 
### Docker Compose
Run the following command from project root directory.
```sh
docker compose up
``` 
### Kubernetes
0. Prerequisites
Kubernetes cluster must have priority class *user-normal* defined.
Kubernetes cluster must have *container-registry-pullsecret* pull secret for GHCR defined in the target namespace *example-net*.

1. Install the remote Helm Chart
Login to GHCR previosuly.
```sh
helm install example-net oci://ghcr.io/wss-ea-cloud-devops/example-net-helm --version 0.1.10 --namespace example-net --create-namespace
``` 

2. Install the local Helm Chart
If the Helm chart is downloaded or build locally as a *.tgz* file
```sh
helm install example-net example-net-helm-0.1.10.tgz --namespace example-net --create-namespace
``` 

## Test
Application starts listening at HTTP port 50001 by default.
Applications responds with HTTP status code *200 OK* at the following endpoints:
- /
- /healthz
- /livez
- /readyz
