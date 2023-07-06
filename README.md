# example-net

Eample .NET application

- [example-net](#example-net)
  - [Build solution](#build-solution)
    - [Visual Studio](#visual-studio)
    - [Command line](#command-line)
  - [Build container](#build-container)
    - [Using CLI](#using-cli)
      - [Docker image](#docker-image)
      - [Helm chart](#helm-chart)
    - [GitHub actions](#github-actions)
  - [Configuration](#configuration)
    - [Logging](#logging)
    - [Elastic APM](#elastic-apm)
    - [Configuration examples](#configuration-examples)
      - [Local configuration](#local-configuration)
      - [Docker config](#docker-config)
      - [Kubernetes config](#kubernetes-config)
  - [Run the application](#run-the-application)
    - [Executable](#executable)
    - [Docker](#docker)
    - [Docker Compose](#docker-compose)
    - [Kubernetes](#kubernetes)
      - [Prerequisites](#prerequisites)
      - [Custom values.yaml example](#custom-valuesyaml-example)
      - [Install the remote Helm Chart](#install-the-remote-helm-chart)
      - [Install the local Helm Chart](#install-the-local-helm-chart)
  - [Test application](#test-application)

## Build solution

### Visual Studio

Just build the solution in Visual Studio.

### Command line

Run the following command from the solution's directory:

```sh
dotnet build --configuration Release
```

The build result as an executable is available at ```<solution root>/ExampleNet/bin/<Debug|Release>/net7.0/```

## Build container

### Using CLI

#### Docker image

Run the following command from the solution's directory:

```sh
docker build -t example-net-docker .
```

#### Helm chart

Run the following commands from the *./helm* directory:

```sh
helm lint --strict
helm package ./
```

### GitHub actions

Application will be build and packed automatically after pushing a new tag.

Tag's name **must** start with **v** letter. For example **v1.2.3**

Docker image and Helm chart will be versioned after the tag:

- example-net-docker:1.2.3
- example-net-helm:1.2.3

Both the Docker image and the Helm chart will be pushed to the GitHub Container Registry for *WSS-EA-Cloud-DevOps* organizationn.

## Configuration

### Logging

The following logging options are supported:

- Console
- Debug window in Visual Studio
- Seq
- Syslog TCP/UDP
- Elasticsearch

### Elastic APM

Elastic APM module can be switched on and off with *ElasticApm:Enabled* option.

### Configuration examples

#### Local configuration

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
          "connectionGlobalHeaders": "Authorization=Basic login:passwordbase64="
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithElasticApmCorrelationInfo" ]
  }
}
```

#### Docker config

Local container configuration

1. Set local environment variable **DOTNET_ENVIRONMENT=localcontainer**
When start application from Visual Studion this environment variable is configured in **launchSettings.json** file.

2. Configure settings in **appsettings.localcontainer.json** file

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

#### Kubernetes config

## Run the application

### Executable

Run executable *ExampleNet.exe* file from it's location.

### Docker

1. Pull the Docker image *example-net-docker* or build the image locally.
2. Run container

```sh
docker run --publish 50001:8080 --env DOTNET_ENVIRONMENT=localcontainer example-net-docker
```

### Docker Compose

Run the following command from solution's root directory. This will build a new container image and run it.

```sh
docker compose up
```

### Kubernetes

#### Prerequisites

- Kubernetes cluster must have priority class *user-normal* defined.
- Kubernetes cluster must have *container-registry-pullsecret* pull secret for the GHCR defined in the target namespace *example-net*.

All the WSS DevOps platform Kubernetes clusters satisfied all requirements.

Priority classes are defined here ```k8s-argo-mgmt-cl1-sol/platform/common/1/platform.yaml```

Ask Cloud&DevOps team *container-registry-pullsecret* pull secret for the GHCR.

Application's configuration can be done using the custom *--values values.yaml*

#### Custom values.yaml example

```yaml
settings:
  timeSpan: 5
  logLevel: Information

elastic:
  log:
    enabled: true
    nodeUris: "https://elastic-mp-platform-sandbox.es.westeurope.azure.elastic-cloud.com"
    indexFormat: "example-net-index-{0:yyyy}"
    connectionGlobalHeaders: "Authorization=Basic login:passwordbase64="
  apm:
    enabled: true
    serverUrls: "https://elastic-mp-platform-sandbox.apm.westeurope.azure.elastic-cloud.com"
    serviceName: "example-net"
    environment: prod
    metricsInterval: 2m
    apiKey: "dummy=="
    
keyvault:
  enabled: false
  name: kv-mp-example-net-sol
  userAssignedIdentityID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  tenantId: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```  

**NB!** Values from a Key Vault has preference over values from *values.yaml* config file.

#### Install the remote Helm Chart

Login to GHCR previosuly.

```sh
helm install example-net oci://ghcr.io/wss-ea-cloud-devops/example-net-helm --version 0.1.10 --namespace example-net --create-namespace --values values.yaml
```

#### Install the local Helm Chart

If the Helm chart is downloaded or build locally as a *.tgz* file.

```sh
helm install example-net example-net-helm-0.1.10.tgz --namespace example-net --create-namespace  --values values.yaml
```

## Test application

Application starts listening at HTTP port **50001** by default.

Applications responds with HTTP status code *200 OK* at the following endpoints:

- /
- /healthz
- /livez
- /readyz
