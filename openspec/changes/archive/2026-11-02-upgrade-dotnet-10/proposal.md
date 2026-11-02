## Why

.NET 8 reaches end of support on **2026-11-10**. After that date the service runs on a runtime that no longer gets security patches. The dependency graph also carries known vulnerabilities:
- **High:** Snappier 1.0.0 and System.Text.Json 8.0.0
- **Moderate:** SharpCompress 0.30.1

In addition, the MSTest 3.1.1 packages are marked *Legacy*. Moving to .NET 10 (LTS, supported until 2028-11-14) and current package versions fixes all of these.

## What Changes

- Retarget `ExampleNet` and `ExampleNetTests` from `net8.0` to `net10.0`.
- Remove the `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Configuration.UserSecrets` package references. On `net10.0` the ASP.NET Core shared framework provides them, and the SDK flags them as redundant (NU1510).
- Upgrade every remaining package reference to its current version, including these major-version jumps:
  - Serilog 3 → 4
  - Serilog.Extensions.Hosting and Serilog.Settings.Configuration 8 → 10
  - Serilog.Sinks.Elasticsearch 9 → 10
  - Elastic.Apm.NetCoreAll 1.25 → 1.36
  - Elastic.Serilog.Sinks and Elastic.Apm.SerilogEnricher 8.6 → 9.0
  - MSTest 3 → 4, Microsoft.NET.Test.Sdk 17 → 18, coverlet.collector 6 → 10
- Replace the Elastic APM host-builder registration, which is obsolete in APM 1.36, with the service-collection registration. The instrumentation set stays the same.
- Container build image moves to the .NET 10 SDK. The runtime image moves from `aspnet:8.0-jammy-chiseled` (Ubuntu 22.04) to `aspnet:10.0-noble-chiseled` (Ubuntu 24.04), because no `jammy` image exists for .NET 10.
- Update the framework references in `README.md` and `CLAUDE.md`.

There are no breaking changes for consumers. The HTTP endpoints, the port (50001) and the environment-variable configuration contract used by the Helm chart all stay the same.

## Capabilities

### New Capabilities

- `service-runtime`: the platform guarantees the service gives the systems that build, scan and deploy it:
  - it runs on a supported LTS runtime;
  - its dependencies pass a vulnerability audit;
  - its HTTP endpoints and port are unchanged;
  - it honours the deployment configuration contract;
  - its container runs non-root with a read-only root filesystem.

### Modified Capabilities

None. The repository has no existing specs.

## Impact

- **Code:**
  - `ExampleNet/ExampleNet.csproj`
  - `ExampleNetTests/ExampleNetTests.csproj`
  - `ExampleNet/Program.cs` (APM registration only)
- **Container:** `Dockerfile`. The runtime image's base OS changes from Ubuntu 22.04 to 24.04 (chiseled), which image scanners and base-image policies will notice. The Build-Docker workflow picks up the new image on the next `v*` tag.
- **Docs:** `README.md`, `CLAUDE.md`.
- **Unchanged:**
  - the Helm chart, `docker-compose.yml` and `pod-example.yaml`;
  - the CI workflows, which have no `dotnet` steps. The Docker build stays the only CI compile gate.
- **Developer environment:** needs a .NET 10 SDK (10.0.402 is installed locally).
