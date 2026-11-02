## 1. Project files

- [x] 1.1 In `ExampleNet/ExampleNet.csproj` and `ExampleNetTests/ExampleNetTests.csproj`, change `<TargetFramework>` from `net8.0` to `net10.0` (design D1). Verify with `grep -n TargetFramework ExampleNet/ExampleNet.csproj ExampleNetTests/ExampleNetTests.csproj`: both lines must show `net10.0`.
- [x] 1.2 Remove the `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Configuration.UserSecrets` `PackageReference` items from `ExampleNet/ExampleNet.csproj` (design D2). Keep `<UserSecretsId>`. Verify that `dotnet build` output has no `NU1510`.
- [x] 1.3 Set every remaining `PackageReference` version to the "To" column of design D3, editing both `.csproj` files in one change (not `dotnet add package` one at a time). Verify:
  - `dotnet restore ExampleNet.sln` succeeds with no `NU1605`.
  - `dotnet list ExampleNet.sln package --outdated` lists nothing newer than a patch release of those versions.

## 2. Code and build

- [x] 2.1 In `ExampleNet/Program.cs`, remove `hostBuilder.UseElasticApm();` and call `services.AddElasticApm();` inside the existing `ConfigureServices` callback (design D5). Do not use `AddAllElasticApm`. Verify that `dotnet build` output has no `CS0618`.
- [x] 2.2 Run `dotnet restore ExampleNet.sln`, then `dotnet build ExampleNet.sln --no-restore`, then `dotnet test ExampleNet.sln --no-build`. Verify:
  - The build succeeds, and its only warning is the existing `CS8605` in `TimedHostedService.cs`.
  - `SettingsTests.SettingsTest` passes (1/1) on `net10.0`.
- [x] 2.3 Run `dotnet list ExampleNet.sln package --vulnerable --include-transitive`. Verify it reports no vulnerable packages for either `ExampleNet` or `ExampleNetTests` (spec: No known vulnerable dependencies).

## 3. Container image

- [x] 3.1 In `Dockerfile`:
  - change the build stage to `mcr.microsoft.com/dotnet/sdk:10.0`;
  - change the runtime stage to `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`;
  - keep `EXPOSE 50001/tcp` and `ENV DOTNET_EnableDiagnostics=0` (design D6).

  Verify that `docker build -t example-net-docker .` succeeds. This needs a running Docker engine. If none is available, record 3.1 to 3.3 as **not run**, with the error, instead of checking them off.
- [x] 3.2 Run `docker run --rm -p 50001:50001 example-net-docker` and verify that `curl -s -o /dev/null -w "%{http_code}" http://localhost:50001/healthz` returns `200`. Also verify that `docker image inspect example-net-docker --format '{{range .Config.Env}}{{println .}}{{end}}'` shows `DOTNET_VERSION=10.0.x` and `ASPNET_VERSION=10.0.x` (spec: HTTP endpoints and port, Supported runtime).
- [x] 3.3 Run `docker run --rm -p 50001:50001 --read-only --user 1000:1000 example-net-docker` and verify that `GET /healthz` on port 50001 returns `200` (spec: Container runs under a restricted security context).

## 4. Runtime behaviour checks (spec: service-runtime)

These run against the local build output (`ExampleNet/bin/Debug/net10.0`). If port 50001 is busy, set `Kestrel__EndPoints__Http__Url=http://localhost:<free-port>`.

- [x] 4.1 Start the app (`dotnet run --project ExampleNet`). Verify:
  - `GET /`, `/healthz`, `/readyz` and `/livez` each return `200`, and `GET /` returns `Ok`.
  - `ExampleNet/bin/Debug/net10.0/ExampleNet.runtimeconfig.json` shows `"tfm": "net10.0"` and a `Microsoft.AspNetCore.App` framework at version `10.0.x`.
  - Do not use the startup log for this: the `.NET Version:` line is written at Debug through the bootstrap logger and is suppressed.
- [x] 4.2 Start the app with `Settings__TimeSpan=1` and verify that periodic `LogLevel=... Count=...` entries appear about once per second. Restart with `Settings__TimeSpan=1 Serilog__MinimumLevel=Warning` and verify:
  - no periodic entries below Warning are written;
  - the bootstrap/config-dump lines written before the host starts are exempt, because they come from the bootstrap logger.
- [x] 4.3 Start the app with these variables:
  - `Serilog__WriteTo__1__Name=Elasticsearch`
  - `Serilog__WriteTo__1__Args__nodeUris=http://localhost:59200` (nothing listening)
  - `Serilog__WriteTo__1__Args__indexFormat=example-net-{0:yyyy}`

  Verify:
  - The self-log output (stderr) shows the Elasticsearch sink trying `localhost:59200`. The stack trace goes through `Serilog.Sinks.Elasticsearch...ElasticsearchVersionManager`.
  - There is no `Unable to find a method called Elasticsearch` or similar binding error.
  - Note: the line `Required properties not provided for: Serilog:WriteTo:1:Args:indexFormat=example-net-{0:yyyy}` already appears on net8. It is not a regression; do not chase it (design, Spike evidence).
- [x] 4.4 Start a local HTTP listener (`python -m http.server 58200`), then start the app with `ElasticApm__Enabled=true ElasticApm__ServerUrls=http://localhost:58200`. Verify the listener logs requests from the APM agent, such as `/intake/v2/events` or `/config/v1/agents`. Restart the app without those variables (the default `Enabled=false`) and verify the listener receives no requests within 60 seconds (design, APM risk).
- [x] 4.5 Check `https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json` and verify that channel `10.0` shows `release-type: lts`, `support-phase: active` and an `eol-date` after today (spec: Supported runtime).

## 5. Documentation

- [x] 5.1 In `README.md`, change the build output path `bin/<Debug|Release>/net7.0/` to `net10.0`. Verify that `grep -nE "net7\.0|net8\.0" README.md` returns nothing.
- [x] 5.2 In `CLAUDE.md`:
  - change ".NET 8 generic-host app" to ".NET 10";
  - change the build output comment to `net10.0`;
  - rewrite the `dotnet-developer` **Target framework** bullet, since the repo now matches the agent's .NET 10 baseline and the Dockerfile uses 10.0 images;
  - in the README-staleness note, drop the "net8.0, not net7.0" sentence and keep the `docker run` port remark;
  - in "Configuration flow", change "Elastic APM is wired with `UseElasticApm()`" to the `AddElasticApm()` registration from task 2.1. This item was added during apply; the line goes stale for the same reason as the others (design D7).

  Verify that `grep -nE "net8\.0|\.NET 8|8\.0-jammy|UseElasticApm" CLAUDE.md` returns nothing.
