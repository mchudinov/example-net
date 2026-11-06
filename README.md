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
      - [Install using Helm chart](#install-using-helm-chart)
        - [Custom values.yaml example](#custom-valuesyaml-example)
        - [Install the remote Helm Chart](#install-the-remote-helm-chart)
        - [Install the local Helm Chart](#install-the-local-helm-chart)
      - [Run as a single pod](#run-as-a-single-pod)
  - [Test application](#test-application)

## Build solution

### Visual Studio

Open the solution ```src/ExampleNet.slnx``` in Visual Studio and build it.

### Command line

Run the following command from the repository root:

```sh
dotnet build src/ExampleNet.slnx --configuration Release
```

The build result as an executable is available at ```<repository root>/src/ExampleNet/bin/<Debug|Release>/net10.0/```

## Build container

### Using CLI

#### Docker image

Run the following command from the repository root:

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

Docker images are published for `linux/amd64` and `linux/arm64`, starting with the first release after 1.2.1. Version 1.2.1 and earlier are `linux/amd64` only.

Both the Docker image and the Helm chart will be pushed to the GitHub Container Registry under the *mchudinov* account. Both packages are public.

## Configuration

### Logging

The following logging options are supported:

- Console
- Debug window in Visual Studio
- Seq
- Syslog TCP/UDP

### Configuration examples

#### Local configuration

1. Set local environment variable **DOTNET_ENVIRONMENT=local**
When start application from Visual Studion this environment variable is configured in **launchSettings.json** file.

2. Configure settings in **appsettings.local.json** in the *src/ExampleNet/* directory. Docker builds never copy this file into the image.

```json
{
  "Settings": {
    "TimeSpan": 2,
    "ShutdownTimeout": 10
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName" ]
  }
}
```

#### Docker config

Local container configuration

1. Set local environment variable **DOTNET_ENVIRONMENT=localcontainer**
When start application from Visual Studion this environment variable is configured in **launchSettings.json** file.

2. Configure settings in **appsettings.localcontainer.json** file in the *src/ExampleNet/* directory. A locally built image includes it.

```json
{
  "Settings": {
    "TimeSpan": 2,
    "ShutdownTimeout": 10
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName" ]
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

Run the following command from the repository root. This will build a new container image and run it.

```sh
docker compose up
```

### Kubernetes

#### Prerequisites

- Kubernetes cluster must have priority class *user-normal* defined.

All the WSS DevOps platform Kubernetes clusters satisfied all requirements.

Priority classes are defined here ```k8s-argo-mgmt-cl1-sol/platform/common/1/platform.yaml```

Application's configuration can be done using the custom *--values values.yaml*

#### Install using Helm chart

##### Custom values.yaml example

```yaml
settings:
  timeSpan: 5
  logLevel: Information
```  

##### Install the remote Helm Chart

Chart versions 1.2.1 and earlier default to an old image location. To install one of them, add `--set image.repository=ghcr.io/mchudinov/example-net-docker`.

```sh
helm install example-net oci://ghcr.io/mchudinov/example-net-helm --namespace example-net --create-namespace --values values.yaml
```

##### Install the local Helm Chart

If the Helm chart is downloaded or build locally as a *.tgz* file.

```sh
helm install example-net example-net-helm-0.1.10.tgz --namespace example-net --create-namespace  --values values.yaml
```

#### Run as a single pod

A single pod example is given in the source code ```pod-example.yaml```

```sh
kubectl apply -f pod-example.yaml
```

## Test application

Application starts listening at HTTP port **50001** by default.

Applications responds with HTTP status code *200 OK* at the following endpoints:

- /
- /healthz
- /livez
- /readyz
