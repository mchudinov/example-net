# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A minimal .NET 10 generic-host app (`ExampleNet`). It serves as a reference service for building, signing, and deploying to Kubernetes via Docker + Helm. It does no real work. A timer writes log lines at a random level, and Kestrel serves `/` (returns `Ok`) plus the health endpoints `/healthz`, `/readyz` and `/livez`, all on port **50001**.

## C# development goes to the `dotnet-developer` subagent

Delegate all C# work to the `dotnet-developer` subagent (`.claude/agents/dotnet-developer.md`). Do not edit these files directly in the main session:

- `*.cs`, `*.csproj`, `*.sln`
- `ExampleNet/appsettings*.json`
- anything in `ExampleNetTests/`

Helm, Dockerfile, workflow and documentation changes stay in the main session.

- **Branching.** The agent creates a local `agent/<slug>` branch and commits there. It never pushes or opens PRs unless asked. Afterwards the working tree is left on that branch.
- **Target framework.** The repo targets `net10.0`, which matches the agent's .NET 10 baseline. Three things must change together with the target framework: the Dockerfile's `sdk:10.0` build image, its `aspnet:10.0-noble-chiseled` runtime image, and the SDK floor in `global.json`.
- **Chart changes.** If a C# change needs a matching chart change, include the chart change in the same brief so both land in one commit. Example: a new `Settings` value that needs an env var in `helm/templates/deployment.yaml`.

## Commands

Run from the repo root unless noted.

```sh
dotnet build                                   # output: ExampleNet/bin/<Debug|Release>/net10.0/
dotnet test                                    # MSTest.Sdk project ExampleNetTests, on Microsoft.Testing.Platform
dotnet test --filter "FullyQualifiedName~SettingsTests.SettingsTest"   # single test; a filter matching nothing exits 8
dotnet test --coverage --coverage-output-format cobertura --report-trx  # results in TestResults/ (gitignored)
dotnet run --project ExampleNet                # launch profile sets DOTNET_ENVIRONMENT=local

docker build -t example-net-docker .
docker compose up                              # host 8080 -> container 50001

# from helm/ (Helm 4 requires the path argument; the README form `helm lint --strict` fails)
helm lint . --strict
helm template t .                              # inspect the rendered manifests and env wiring
```

The README's `docker run --publish 50001:8080` example is wrong: the container listens on 50001, so publish `<host>:50001`.

**Test runner.**
- `global.json` does two things:
  - It selects Microsoft.Testing.Platform as the `dotnet test` runner. Without that setting, `dotnet test` on the .NET 10 SDK fails for an `MSTest.Sdk` project, so change and revert `ExampleNetTests.csproj` and `global.json` together.
  - It requires a 10.0 SDK: at least 10.0.100, with the newest installed 10.0 SDK used (`latestFeature`).
- VSTest-only options such as `--collect` are rejected. For coverage, use `--coverage`.

**Container build.**
- The Dockerfile restores and publishes only `ExampleNet/ExampleNet.csproj`, so no test files reach the image.
- The test project also sets `IsPublishable=false`, so a solution-level `dotnet publish` skips it.

## Configuration flow

- `Program.Main` builds its **own** `ConfigurationBuilder`: `appsettings.json` → `appsettings.{DOTNET_ENVIRONMENT}.json` (optional) → environment variables. It then feeds that into the host. It keys on `DOTNET_ENVIRONMENT`, not `ASPNETCORE_ENVIRONMENT`.
- `appsettings.local*.json` (the `local` and `localcontainer` environments) is gitignored. Create it locally; the README has full examples.
- Logging is configured entirely from the `Serilog` section via `ReadFrom.Configuration`. A `WriteTo` or `Enrich` entry naming a method from a package that isn't referenced doesn't fail startup. Serilog skips it and writes `Unable to find a method called ...` to stderr.
- At startup the app logs every environment variable (Debug) and every resolved config key=value (Information). Secrets supplied through config will appear in the logs.
- `Settings` gotchas:
  - `Settings.ShutdownTimeout` is never used. Host shutdown is hard-coded to 5s in `Program.cs`.
  - `TimedHostedService` re-reads the `Settings` section from `IConfiguration` instead of using the `Settings` singleton registered in DI.

## Health checks

All three health endpoints run the same check, `StartupReadyCheck`. It reports unhealthy until `TimedHostedService.StartAsync` sets `StartupCompleted = true`. A background-service exception stops the host (`BackgroundServiceExceptionBehavior.StopHost`).

## Helm chart (`helm/`)

- **Values → app config.** `templates/deployment.yaml` maps chart values to .NET env vars using the `Section__Key` convention. It emits only two: `Settings__TimeSpan` (from `settings.timeSpan`) and `Serilog__MinimumLevel` (from `settings.logLevel`). Of the `Settings` values, only `TimeSpan` is exposed.
- **Removed-values guard.** `templates/removed-values.yaml` renders no manifest. It fails `helm template`, `install` and `upgrade` when `elastic.log.enabled`, `elastic.apm.enabled` or `keyvault.enabled` is true. Those features (Elasticsearch logging, Elastic APM and Azure Key Vault via CSI) were removed.
  - `helm lint` only logs the `fail` at INFO level, so test the guard with `helm template`.
  - Delete the file once deployers have migrated.
- **Argo Rollouts** (`rollout.enabled`). This swaps the Deployment for `rollout-bluegreen.yaml` plus preview service/ingress. `rollout-canary.yaml` is an empty stub. The blue-green template is out of sync with `deployment.yaml`: it uses containerPort 8080, probes `/`, and has no env vars. Changes to `deployment.yaml` are not reflected there.
- **Cluster prerequisites.** The chart expects priority class `user-normal` and pull secret `container-registry-pullsecret` in the target namespace.

## CI/CD and versioning (`.github/workflows/`)

1. **Build-Dotnet** runs on pushes and PRs to `main` that touch any of these paths:
   - `ExampleNet/**`
   - `ExampleNetTests/**`
   - `ExampleNet.sln`
   - `global.json`
   - its own workflow file

   It runs `dotnet restore`, `dotnet build -c Release` and `dotnet test`, then uploads `TestResults/` (TRX and cobertura) as an artifact. `--report-gh` writes the test summary to the run page. Inline annotations do not come through `dotnet test`, so look in the summary for failures.
2. Pushing a `v*` tag runs **Build-Docker**. Its first job, `Test`, calls Build-Dotnet as a release gate. Everything after it runs only if the tests pass:
   - build and push `ghcr.io/<owner>/example-net-docker:<version>`;
   - sign it with cosign (keyless, no Rekor);
   - have a bot rewrite `helm/Chart.yaml` `version` and `appVersion` to the tag version and commit to `main`.
3. **Build-Helm** runs in three situations: when Build-Docker completes, on pushes and PRs to `main` that touch `helm/**`, and on manual runs. After a Build-Docker run, it continues only if that run succeeded. Build-Helm finds Build-Docker by name, so don't rename that workflow.
   - **Every run checks the chart:**
     - `helm lint . --strict`
     - install into a k3d cluster
     - Fairwinds Polaris audit with `.github/polaris-config.yaml`; fails on any "danger" finding or a score below 90
   - **Only a run that follows a successful Build-Docker (a release) publishes:**
     - push the chart to GHCR as OCI `example-net-helm`
     - cosign-sign the chart

     Pushes, PRs and manual runs publish nothing. So a released chart version is never overwritten, even though `main`'s chart can get ahead of the last release.
   - **If a release's chart publishing fails,** fix the cause and push the next patch tag. A manual run can't publish.

Do not hand-edit the `Chart.yaml` version fields; the tag drives them. Changes to the chart's securityContext, probes or resources must keep the Polaris audit passing.

## OpenSpec

The repo has an OpenSpec spec-driven workflow (`openspec/`, plus `/opsx:*` commands and `openspec-*` skills in `.claude/`). Main specs live in `openspec/specs/` (currently `service-runtime` and `ci-test-gate`), and finished changes go to `openspec/changes/archive/`.
