## Context

See proposal.md (Why) for the motivation. This is the current state that shapes the approach:

- **Projects.** Two SDK-style projects, both `net8.0`: `ExampleNet` (Web SDK) and `ExampleNetTests` (MSTest). They sit in a single `ExampleNet.sln`, with no `global.json` and no central package management.
- **Hosting.** `Program.cs` builds the host with `Host.CreateDefaultBuilder` + `ConfigureWebHostDefaults`. It registers APM with `hostBuilder.UseElasticApm()`, with no diagnostic subscribers. Logging is configured entirely from the `Serilog` section via `ReadFrom.Configuration`.
- **Elasticsearch packages.** The app references two Elasticsearch logging packages, `Serilog.Sinks.Elasticsearch` and `Elastic.Serilog.Sinks`, and both define a `WriteTo.Elasticsearch` method. The Helm chart passes `nodeUris`, `indexFormat` and `connectionGlobalHeaders`. Those argument names select the `Serilog.Sinks.Elasticsearch` overload.
- **Container.** The `Dockerfile` builds with `sdk:8.0` and runs on `aspnet:8.0-jammy-chiseled`. The listen port 50001 comes from `Kestrel:EndPoints` in `appsettings.json`, not from the image.
- **Validation gaps.**
  - CI never runs `dotnet build` or `dotnet test`. The Docker build is the only compile gate.
  - The only test (`SettingsTests`) checks `Settings` defaults. It does not exercise logging, APM or HTTP, so runtime checks have to be manual.

### Spike evidence

Three spikes were run in scratch copies of the repo and against the current build, before planning:

- **Spike A: retarget only.** The build passed and 1/1 tests passed.
  - Warnings: NU1510 for `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Configuration.UserSecrets`.
  - Vulnerabilities: the System.Text.Json advisory is gone (the shared framework now supplies it). Snappier (High) and SharpCompress (Moderate) remain, via `Elastic.Apm.NetCoreAll 1.25.3 → Elastic.Apm.MongoDb → MongoDB.Driver.Core 2.19.0`.
- **Spike B: retarget, remove the two NU1510 packages, and bump all packages to latest.**
  - Build passed and 1/1 tests passed. The transitive vulnerability audit was clean.
  - Warnings: only CS0618 (`UseElasticApm` is obsolete) and the existing CS8605.
  - At runtime: `/`, `/healthz`, `/readyz` and `/livez` all returned 200, and with the Helm-style `Serilog__WriteTo__1__*` variables set, the `Serilog.Sinks.Elasticsearch` sink bound.
- **Baseline: the current net8 build with the same variables.** The same sink bound. The same `Required properties not provided for: Serilog:WriteTo:1:Args:indexFormat=example-net-{0:yyyy}` self-log line appeared. That line comes from the startup config dump, which logs each `key=value` string as a message template. It is **not** a regression.
- **APM baseline: the current net8 build against a local HTTP listener on 127.0.0.1:58200.**
  - With `ElasticApm__Enabled=true` and `ElasticApm__ServerUrls` pointing at the listener, the agent sent `GET /`, `GET /config/v1/agents?service.name=example-net&service.environment=Development` and `POST /intake/v2/events`. The app's error traces were only the agent failing to parse the listener's HTML as JSON.
  - With the default configuration (APM disabled), the listener received no requests over 25 seconds.
  - So both monitoring scenarios in the spec describe today's behaviour.
- **Runtime-version evidence.**
  - The app's `.NET Version:` startup line is written at Debug through the bootstrap logger, whose minimum level is Information. It never appears, so it cannot be used as evidence.
  - Instead, `ExampleNet.runtimeconfig.json` from Spike B shows `"tfm": "net10.0"` and `Microsoft.AspNetCore.App` `10.0.0`.
  - The registry config for `aspnet:10.0-noble-chiseled` (linux/amd64) sets `DOTNET_VERSION=10.0.14` and `ASPNET_VERSION=10.0.14`, plus `ASPNETCORE_HTTP_PORTS=8080` and user 1654, the same defaults as the 8.0 chiseled image.

## Goals / Non-Goals

**Goals:**
- Make a like-for-like platform upgrade. The `service-runtime` contract (specs/service-runtime/spec.md) must hold with no behaviour change.
- After the upgrade, the build shows no warnings except the existing CS8605 in `TimedHostedService.cs`.

**Non-Goals:**
- Migrating log shipping to `Elastic.Serilog.Sinks`, which would change the configuration shape and the Helm chart. Removing the unused-at-runtime `Elastic.Serilog.Sinks` reference is also out of scope.
- Enabling more APM instrumentation (`AddAllElasticApm`).
- Converting `.sln` to `.slnx`, adding central package management, or adding a `global.json`.
- Fixing the existing CS8605 warning, the config-dump message-template issue, the Argo Rollouts template drift, or the README's `docker run` port example.
- Adding `dotnet build`/`dotnet test` steps to CI.

## Decisions

### D1. Target `net10.0` with a single target framework

.NET 10 is the current LTS (active until 2028-11-14). The alternatives are worse:
- **.NET 9** is an STS release that also ends on 2026-11-10.
- **Multi-targeting `net8.0;net10.0`** would keep a runtime alive that loses support in days, and gives this app nothing.

### D2. Drop `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Configuration.UserSecrets`

`ExampleNet` uses `Microsoft.NET.Sdk.Web`, so the ASP.NET Core shared framework already provides both. On `net10.0` the SDK warns (NU1510) that these references will not be pruned.

Keeping them with bumped versions would only add redundant references. Spike A produced the warning and Spike B confirmed that the build and runtime are fine without them.

### D3. Bump every package to its current version, majors included

A retarget alone leaves High/Moderate advisories in place (Spike A) and keeps the *Legacy* MSTest 3.1.1. Bumping everything gave a green build, green tests and a clean audit (Spike B). Versions found at planning time:

| Package | From | To |
|---|---|---|
| Elastic.Apm.NetCoreAll | 1.25.3 | 1.36.0 |
| Elastic.Apm.SerilogEnricher | 8.6.1 | 9.0.0 |
| Elastic.Serilog.Sinks | 8.6.1 | 9.0.0 |
| Serilog | 3.1.1 | 4.4.0 |
| Serilog.Enrichers.Environment | 2.3.0 | 3.0.1 |
| Serilog.Extensions.Hosting | 8.0.0 | 10.0.0 |
| Serilog.Formatting.Compact | 2.0.0 | 3.0.0 |
| Serilog.Settings.Configuration | 8.0.0 | 10.0.1 |
| Serilog.Sinks.Console | 5.0.1 | 6.1.1 |
| Serilog.Sinks.Debug | 2.0.0 | 3.0.0 |
| Serilog.Sinks.Elasticsearch | 9.0.3 | 10.0.0 |
| Serilog.Sinks.Seq | 6.0.0 | 9.1.0 |
| Serilog.Sinks.SyslogMessages | 3.0.1 | 4.1.0 |
| Microsoft.NET.Test.Sdk | 17.8.0 | 18.11.0 |
| MSTest.TestAdapter | 3.1.1 | 4.5.2 |
| MSTest.TestFramework | 3.1.1 | 4.5.2 |
| coverlet.collector | 6.0.0 | 10.1.0 |

A newer patch version at implementation time is acceptable. A newer *major* version is not; it needs a revisit.

Edit the version numbers in each `.csproj` in a single change. Do not run `dotnet add package` once per package. Spike B hit NU1605 downgrade errors at intermediate steps:
- `Elastic.Apm.SerilogEnricher 9` requires `Serilog >= 4.3`.
- `MSTest.TestAdapter 4` requires `MSTest.TestFramework >= 4`.

Alternative considered: retarget only and pin the transitive packages. That leaves the deprecated test framework, and pinning MongoDB driver internals under the APM package is fragile.

### D4. Keep `Serilog.Sinks.Elasticsearch` (upgraded to 10.0.0) as the Elasticsearch sink

It is the sink that actually binds today, on both net8 and net10 (Spike B and the baseline). It accepts the exact `nodeUris`/`indexFormat`/`connectionGlobalHeaders` arguments that the Helm chart and Key Vault secrets supply, so the chart needs no change.

Alternative: `Elastic.Serilog.Sinks`. It is the maintained Elastic sink, but it takes different arguments (`nodes`, data streams). Using it would change `deployment.yaml`, `secretprovider.yaml` and the README examples, so it is deferred to a separate change.

### D5. Register APM through `IServiceCollection.AddElasticApm()`

Replace `hostBuilder.UseElasticApm()` with `services.AddElasticApm()` inside the existing `ConfigureServices` callback.
- It comes from the same package (`Elastic.Apm.Extensions.Hosting` 1.36.0, confirmed in the package's API docs).
- It takes the same (empty) subscriber list.
- It is the replacement the CS0618 message recommends.

Alternatives considered:
- **Keep `UseElasticApm` and suppress CS0618.** The vendor says it "may be removed in a future release".
- **`AddAllElasticApm()`.** This turns on every instrumentation (EF Core, gRPC, MongoDB, Azure SDKs and more), which changes behaviour and overhead.

### D6. Container images: `sdk:10.0` to build, `aspnet:10.0-noble-chiseled` to run

MCR publishes no `jammy` variant for .NET 10. Both `10.0-noble-chiseled` (Ubuntu 24.04) and `10.0-resolute-chiseled` (Ubuntu 26.04) exist.
- `noble` is the default Ubuntu base for .NET 10 images and the more established LTS.
- `resolute` is an acceptable later move.

The image keeps the floating minor tag (`10.0-...`), as the current `8.0-...` tag does. `EXPOSE 50001` and `DOTNET_EnableDiagnostics=0` stay. Port 50001 is still set by `Kestrel:EndPoints` and overrides the image's default `ASPNETCORE_HTTP_PORTS=8080`.

### D7. Update the docs in the same change

The docs record the framework version, and they go stale the moment the retarget lands.
- `README.md` names the build output path with `net7.0`.
- `CLAUDE.md` has four spots: the app description (".NET 8"), the build output path (`net8.0`), the `dotnet-developer` "don't retarget" note, and the README-staleness note.

## Risks / Trade-offs

- **[Risk]** Major bumps to Serilog 4 and Serilog.Settings.Configuration 10 could change how configuration binds, and unit tests don't cover that. → **Mitigation:** runtime checks of the log interval, the minimum level and Elasticsearch sink binding (tasks, group 4). Spike B already showed the sink binding.
- **[Risk]** `AddElasticApm()` may not read the `ElasticApm` configuration section the same way `UseElasticApm()` did. The net8 behaviour is recorded in the APM baseline above: requests are sent when enabled and none when disabled. The net10 + `AddElasticApm()` combination has not been run yet. → **Mitigation:** task 4.4 repeats that baseline against the upgraded build and must show the same on/off behaviour.
- **[Risk]** Two packages expose `WriteTo.Elasticsearch`, and the overload is chosen by argument names. A future argument change could silently switch sinks. → **Mitigation:** the binding check in tasks; the sink migration is deferred (see D4).
- **[Risk]** The base OS change (Ubuntu 22.04 → 24.04) can trip image-scanning or base-image allow-lists. Chiseled images might also behave differently under the chart's read-only root filesystem. → **Mitigation:** a container check with `--read-only --user 1000:1000` (tasks, group 3); call out the base change in the release notes.
- **[Risk]** The Docker engine isn't running on the planning machine, so the container checks may not be runnable locally. → **Mitigation:** report them as *not run* rather than skipping them silently. The Build-Docker workflow builds the image on the next tag.
- **[Trade-off]** MSTest 4 is a major upgrade. The suite is a single trivial test (passed in Spike B), so the risk is low. Test-adapter behaviour under the .NET 10 SDK's `dotnet test` stays on VSTest, the default; no Microsoft.Testing.Platform opt-in is planned.

## Migration Plan

1. Merge the change, then push a `v*` tag.
   - Build-Docker builds and signs the .NET 10 image.
   - The bot bumps `helm/Chart.yaml` `version`/`appVersion`.
   - Build-Helm lints, test-installs, runs Polaris, and pushes and signs the chart.
2. Deploy the new chart version as usual. No data or configuration migration is needed, because the env var contract is unchanged.
3. **Rollback:** `helm rollback` to the previous release, or install the previous chart version (0.2.54), which pins the previous `.NET 8` image by `appVersion`.
