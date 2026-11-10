# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A minimal .NET 10 generic-host app (`ExampleNet`). It serves as a reference service for building, signing, and deploying to Kubernetes via Docker + Helm. It does no real work. A timer writes log lines at a random level, and Kestrel serves `/` (returns `Ok`) plus the health endpoints `/healthz`, `/readyz` and `/livez`, all on port **50001**.

## C# development goes to the `dotnet-developer` subagent

Delegate all C# work to the `dotnet-developer` subagent (`.claude/agents/dotnet-developer.md`). Do not edit these files directly in the main session:

- `*.cs`, `*.csproj`, `*.slnx`
- `src/ExampleNet/appsettings*.json`
- anything in `tests/ExampleNetTests/`

Helm, Dockerfile, workflow and documentation changes stay in the main session.

- **Branching.** The agent creates a local `agent/<slug>` branch and commits there. It never pushes or opens PRs unless asked. Afterwards the working tree is left on that branch.
- **Target framework.** The repo targets `net10.0`, which matches the agent's .NET 10 baseline. Three things must change together with the target framework: the Dockerfile's `sdk:10.0` build image, its `aspnet:10.0-noble-chiseled` runtime image, and the SDK floor in `global.json`. A new runtime image must include `linux/arm64`, because releases publish that platform too.
- **Chart changes.** If a C# change needs a matching chart change, include the chart change in the same brief so both land in one commit. Example: a new `Settings` value that needs an env var in `helm/templates/deployment.yaml`.

## Commands

Run from the repo root unless noted.

**Layout.**
- **Where things are:** the app is `src/ExampleNet/` and the tests are `tests/ExampleNetTests/`.
- **The solution is `src/ExampleNet.slnx`** (the XML solution format). It lists both projects. The test project sits outside `src/`, at `../tests/...`.
- **The repository root has no solution.** Bare `dotnet build` or `dotnet test` there fails with `MSB1003`. Name the solution, as in the commands below, or run the bare commands from `src/`.
- **Keep `src/ExampleNet.slnx` the only solution file in `src/`.** With a second `.sln` or `.slnx` next to it, bare commands in `src/` fail with `MSB1011`. To add a project, run `dotnet sln src/ExampleNet.slnx add <path/to/Project.csproj>`.
- **`global.json` stays at the root.** `dotnet` looks for it from the current folder upward, so it applies whether you run from the root, `src/` or `tests/`. Put any future `Directory.Build.props` at the root too, so it covers both `src/` and `tests/`.

The `dotnet-developer` agent's restore, build and test loop is exactly:

```sh
dotnet restore src/ExampleNet.slnx
dotnet build --no-restore src/ExampleNet.slnx
dotnet test --no-build src/ExampleNet.slnx
```

Other commands:

```sh
dotnet build src/ExampleNet.slnx               # output: src/ExampleNet/bin/<Debug|Release>/net10.0/
dotnet test src/ExampleNet.slnx                # MSTest.Sdk project tests/ExampleNetTests, on Microsoft.Testing.Platform
dotnet test src/ExampleNet.slnx --filter "FullyQualifiedName~SettingsTests.SettingsTest"   # single test; a filter matching nothing exits 8
dotnet test src/ExampleNet.slnx --coverage --coverage-output-format cobertura --report-trx  # results in TestResults/ (gitignored)
dotnet run --project src/ExampleNet            # launch profile sets DOTNET_ENVIRONMENT=local

docker build -t example-net-docker .
docker compose up                              # host 8080 -> container 50001

# from helm/ (Helm 4 requires the path argument; the README form `helm lint --strict` fails)
helm lint . --strict
helm template t .                              # inspect the rendered manifests and env wiring
```

The README's `docker run --publish 50001:8080` example is wrong: the container listens on 50001, so publish `<host>:50001`.

**Test runner.**
- `global.json` does two things:
  - It selects Microsoft.Testing.Platform as the `dotnet test` runner. Without that setting, `dotnet test` on the .NET 10 SDK fails for an `MSTest.Sdk` project, so change and revert `tests/ExampleNetTests/ExampleNetTests.csproj` and `global.json` together.
  - It requires a 10.0 SDK: at least 10.0.100, with the newest installed 10.0 SDK used (`latestFeature`).
- VSTest-only options such as `--collect` are rejected. For coverage, use `--coverage`.

**Container build.**
- **The Dockerfile copies only `global.json` and `src/ExampleNet/`,** in this order:
  1. the project file, then `dotnet restore`;
  2. the source, then `dotnet publish --no-restore`.

  The restore layer is reused until `global.json` or `ExampleNet.csproj` changes. No test files reach the image.
- **The build stage cross-compiles for `linux/amd64` and `linux/arm64`.** The SDK runs natively (`FROM --platform=$BUILDPLATFORM`), and `dotnet restore` and `dotnet publish` both take `-a $TARGETARCH`. Each architecture gets its own restore layer. Without `-a` on restore, the publish fails with `NETSDK1047`. BuildKit sets both variables, so the Dockerfile needs BuildKit, which is the default.
- **Keep the runtime stage free of `RUN`.** A `RUN` there would execute as arm64. The release and check builders have no arm64 emulation, and .NET doesn't support running under QEMU.
- **Root-level build files need their own `COPY`.** Any other root-level file that affects the build, such as a future `Directory.Build.props`, `Directory.Packages.props` or `NuGet.config`, needs its own `COPY` in the Dockerfile. Without one, the image builds with different settings than CI, and nothing reports it. Add the file to `Check-Docker`'s `paths` as well, so a change to it is image-build checked.
- **`.dockerignore` must keep `**/bin` and `**/obj`.** Without them, the source copy overwrites the container's restore output with the host's `obj/project.assets.json`, and the publish fails (MSB4018 about a missing Windows NuGet fallback folder). It also excludes `.git`, `.vs`, `TestResults`, `tests`, `helm`, `openspec` and `appsettings.local.json`.
- **The test project also sets `IsPublishable=false`,** so a solution-level `dotnet publish` skips it.

## Configuration flow

- `Program.Main` builds its **own** `ConfigurationBuilder`: `appsettings.json` → `appsettings.{DOTNET_ENVIRONMENT}.json` (optional) → environment variables. It then feeds that into the host. It keys on `DOTNET_ENVIRONMENT`, not `ASPNETCORE_ENVIRONMENT`.
- `appsettings.local*.json` (the `local` and `localcontainer` environments) is gitignored. Create it locally in `src/ExampleNet/`; the README has full examples.
  - **In Docker builds,** `.dockerignore` keeps `appsettings.local.json` out of the image. It matches that exact name, not `appsettings.local*.json`.
  - **`appsettings.localcontainer.json` is deliberately copied into locally built images,** because `docker compose` sets `DOTNET_ENVIRONMENT=localcontainer`.
  - **CI-built images contain neither file.**
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
- **Default image.** `image.repository` must be `ghcr.io/mchudinov/example-net-docker`, the path Release pushes to. The workflows derive that path from the repo owner while the chart hard-codes it, and CI doesn't compare them, so update the default if the repo moves. Both packages are public, so the chart has no default pull secret.
- **Cluster prerequisites.** The chart expects priority class `user-normal`.

## CI/CD and versioning (`.github/workflows/`)

1. **Build-Dotnet** runs on pushes and PRs to `main` that touch any of these paths:
   - `src/**` (the app and the solution)
   - `tests/**`
   - `global.json`
   - its own workflow file

   It runs `dotnet restore`, `dotnet build -c Release` and `dotnet test` against `src/ExampleNet.slnx`, then uploads `TestResults/` (TRX and cobertura) as an artifact. The test step pins `--results-directory` to the workspace's `TestResults/`, because the upload step only warns when it finds no files. `--report-gh` writes the test summary to the run page. Inline annotations do not come through `dotnet test`, so look in the summary for failures.
2. **Check-Docker** builds the container image to prove the Dockerfile still builds. It runs on pushes and PRs to `main` that touch any of these paths, and on manual runs:
   - `Dockerfile`
   - `.dockerignore`
   - `global.json`
   - `src/**`
   - its own workflow file

   It uses the same `docker/build-push-action` and default builder as Release, and builds `linux/amd64` and `linux/arm64`. Keep its `platforms` list identical to Release's. It only builds:
   - `push: false`;
   - read-only permissions and no registry login;
   - no layer cache;
   - no container run, so it doesn't show that the image starts on either platform.

   It runs in parallel with Build-Dotnet. `tests/**` and `helm/**` changes don't trigger it, because `.dockerignore` keeps `tests/` out of the build.
3. Pushing a `v*` tag runs **Release** (`release.yaml`), which has no manual trigger. One run builds, checks and publishes everything from the tag's commit, and writes nothing to the repository:
   - `Check-Tag` runs first. It fails any tag other than `vX.Y.Z`, including a release candidate such as `v1.3.2-rc.1`, and outputs the version without the `v`.
   - Two gates then run side by side: `Test` calls Build-Dotnet, and `Check-Helm` calls Build-Helm with the tag's version. Nothing is pushed unless both pass.
   - build and push `ghcr.io/<owner>/example-net-docker:<version>` for `linux/amd64` and `linux/arm64` (one multi-platform image, with its `platforms` list identical to Check-Docker's);
   - sign it with cosign (keyless, no Rekor);
   - `Push-Helm` downloads the package `Check-Helm` checked, fails unless its version is the tag's, and pushes that same file to GHCR as OCI `example-net-helm`;
   - `Sign-Helm-Chart` cosign-signs the chart.

   **If a release run fails,** use **Re-run failed jobs** on that same run when the cause was a one-off, such as a stuck k3d cluster. It keeps the tag's commit, and `Push-Helm` reuses the uploaded package, but that path hasn't been used on a release yet. If the cause needs a commit, fix it and push the next patch tag. A failure before the image push publishes nothing; one after it leaves the image without a chart.

   **Never push a release tag on a commit older than the one that added `release.yaml`.** Such a commit still has the old Build-Docker workflow: it would push the image and commit to `main`, and no chart would be published.
4. **Build-Helm** checks the chart and publishes nothing. Release calls it, and it also runs on pushes and PRs to `main` that touch `helm/**`, its own workflow file or `.github/scripts/check-chart-labels.sh`, and on manual runs. Path filters don't apply when Release calls it.
   - **Version:** Release passes the tag's version as the `version` input. Every other run uses `Chart.yaml`'s version. A tag push run with no input fails, so a release can never fall back to the placeholder.
   - **It packages the chart once,** into `$RUNNER_TEMP`, with `--version` and `--app-version` both set to that version, and checks that file:
     - `helm lint --strict`
     - a dry-run install, then an install into a k3d cluster. Neither uses `--wait`: in a release the image doesn't exist yet.
     - build-metadata checks, on a package whose version has the form Flux produces (`<version>+662a06b343e7`):
       - `.github/scripts/check-chart-labels.sh` renders it with two value sets and fails on any invalid label value. Together the sets render every template that sets labels, so a new flag guarding a template with labels must be added to a set in `build-helm.yaml`.
       - install into the same k3d cluster, in its own namespace, because object names come from the chart name and would collide with the first install
     - Fairwinds Polaris audit of the `helm/` directory with `.github/polaris-config.yaml`; fails on any "danger" finding or a score below 90
   - **Last, it uploads the package as the `helm-chart` run artifact.** Release publishes that file. Pushes, PRs and manual runs publish nothing, so a released chart version is never overwritten.

`Chart.yaml`'s `version` and `appVersion` are placeholders that don't follow releases: a release stamps both from its tag when it packages the chart, so don't edit them for a release. Put `.Chart.Version` in a label only through the `app.chart` helper, which replaces `+`. Changes to the chart's securityContext, probes or resources must keep the Polaris audit passing. Reference third-party actions by a release tag or a commit SHA, never a branch. The `setup-polaris` ref only picks the installer; `POLARIS_VERSION` picks the binary.

## OpenSpec

The repo has an OpenSpec spec-driven workflow (`openspec/`, plus `/opsx:*` commands and `openspec-*` skills in `.claude/`). Main specs live in `openspec/specs/` (currently `service-runtime` and `ci-test-gate`), and finished changes go to `openspec/changes/archive/`.
