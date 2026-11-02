# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A minimal .NET 8 generic-host app (`ExampleNet`). It serves as a reference service for building, signing, and deploying to Kubernetes via Docker + Helm. It does no real work. A timer writes log lines at a random level, and Kestrel serves `/` (returns `Ok`) plus the health endpoints `/healthz`, `/readyz` and `/livez`, all on port **50001**.

## C# development goes to the `dotnet-developer` subagent

Delegate all C# work to the `dotnet-developer` subagent (`.claude/agents/dotnet-developer.md`). Do not edit these files directly in the main session:

- `*.cs`, `*.csproj`, `*.sln`
- `ExampleNet/appsettings*.json`
- anything in `ExampleNetTests/`

Helm, Dockerfile, workflow and documentation changes stay in the main session.

- **Branching.** The agent creates a local `agent/<slug>` branch and commits there. It never pushes or opens PRs unless asked. Afterwards the working tree is left on that branch.
- **Target framework.** Its definition says ".NET 10", but this repo targets `net8.0` and the Dockerfile pins the 8.0 `sdk` and `aspnet` images. Tell it not to retarget unless the task is an upgrade.
- **Chart changes.** If a C# change needs a matching chart change, include the chart change in the same brief so both land in one commit. Example: a new `Settings` value that needs an env var in `helm/templates/deployment.yaml`.

## Commands

Run from the repo root unless noted.

```sh
dotnet build                                   # output: ExampleNet/bin/<Debug|Release>/net8.0/
dotnet test                                    # MSTest, project ExampleNetTests
dotnet test --filter "FullyQualifiedName~SettingsTests.SettingsTest"   # single test
dotnet run --project ExampleNet                # launch profile sets DOTNET_ENVIRONMENT=local

docker build -t example-net-docker .
docker compose up                              # host 8080 -> container 50001

# from helm/ (Helm 4 requires the path argument; the README/build-helm.sh form `helm lint --strict` fails)
helm lint . --strict
helm template t . --set elastic.log.enabled=true --set keyvault.enabled=true   # inspect rendered env wiring
```

The README contains some outdated details. The real target framework is net8.0, not net7.0. Its `docker run --publish 50001:8080` example is wrong: the container listens on 50001, so publish `<host>:50001`.

## Configuration flow

- `Program.Main` builds its **own** `ConfigurationBuilder`: `appsettings.json` → `appsettings.{DOTNET_ENVIRONMENT}.json` (optional) → environment variables. It then feeds that into the host. It keys on `DOTNET_ENVIRONMENT`, not `ASPNETCORE_ENVIRONMENT`.
- `appsettings.local*.json` (the `local` and `localcontainer` environments) is gitignored. Create it locally; the README has full examples.
- Logging is configured entirely from the `Serilog` section via `ReadFrom.Configuration`. Elastic APM is wired with `UseElasticApm()` and toggled by `ElasticApm:Enabled`.
- At startup the app logs every environment variable (Debug) and every resolved config key=value (Information). Secrets supplied through config will appear in the logs.
- `Settings` gotchas:
  - `Settings.ShutdownTimeout` is never used. Host shutdown is hard-coded to 5s in `Program.cs`.
  - `TimedHostedService` re-reads the `Settings` section from `IConfiguration` instead of using the `Settings` singleton registered in DI.

## Health checks

All three health endpoints run the same check, `StartupReadyCheck`. It reports unhealthy until `TimedHostedService.StartAsync` sets `StartupCompleted = true`. A background-service exception stops the host (`BackgroundServiceExceptionBehavior.StopHost`).

## Helm chart (`helm/`)

- **Values → app config.** `templates/deployment.yaml` maps chart values to .NET env vars using the `Section__Key` convention (`Settings__TimeSpan`, `Serilog__MinimumLevel`, `ElasticApm__*`). Of the `Settings` values, only `TimeSpan` is exposed.
- **Elasticsearch sink.** It is injected as `Serilog__WriteTo__1__*`. That index assumes `appsettings.json` defines exactly one sink (Console) at index 0. Adding a sink there shifts the index, so update the chart to match.
- **Key Vault** (`keyvault.enabled`):
  - `secretprovider.yaml` (Azure CSI) syncs Key Vault objects into k8s secrets named `elasticapm-apikey` and `serilog-writeto-1-args-connectionglobalheaders`.
  - The deployment then emits the same env var names a second time from those secrets. Key Vault values override `values.yaml` only because the later entry wins.
  - Secret names must match across both templates.
- **Argo Rollouts** (`rollout.enabled`). This swaps the Deployment for `rollout-bluegreen.yaml` plus preview service/ingress. `rollout-canary.yaml` is an empty stub. The blue-green template is out of sync with `deployment.yaml`: it uses containerPort 8080, probes `/`, and has no env vars or Key Vault mounts. Changes to `deployment.yaml` are not reflected there.
- **Cluster prerequisites.** The chart expects priority class `user-normal` and pull secret `container-registry-pullsecret` in the target namespace.

## CI/CD and versioning (`.github/workflows/`)

1. Pushing a `v*` tag runs **Build-Docker**. It builds and pushes `ghcr.io/<owner>/example-net-docker:<version>` and signs it with cosign (keyless, no Rekor). It then has a bot rewrite `helm/Chart.yaml` `version` and `appVersion` to the tag version and commit to `main`.
2. **Build-Helm** runs when Build-Docker completes, and on pushes/PRs touching `helm/**`. Its steps:
   - `helm lint . --strict`
   - install into a k3d cluster
   - Fairwinds Polaris audit with `.github/polaris-config.yaml`; fails on any "danger" finding or a score below 90
   - push the chart to GHCR as OCI `example-net-helm`
   - cosign-sign the chart

Do not hand-edit the `Chart.yaml` version fields; the tag drives them. Changes to the chart's securityContext, probes or resources must keep the Polaris audit passing.

## OpenSpec

The repo has an OpenSpec spec-driven workflow (`openspec/`, plus `/opsx:*` commands and `openspec-*` skills in `.claude/`). Both directories are currently untracked, and `openspec/specs/` is empty.
