## Why

The maintainer has decided to remove Elastic from the reference service. That covers both Elasticsearch log shipping and Elastic APM.

Both are off by default today:
- In the app, `ElasticApm:Enabled` is `false` and `appsettings.json` configures only the Console sink.
- In the chart, `elastic.log.enabled` and `elastic.apm.enabled` are both `false`.

Even so, the app's four Elastic package references bring in 33 of its 46 resolved packages, for example MongoDB.Driver, SharpCompress, Elasticsearch.Net and twelve `Elastic.Apm.*` packages. One of the four, `Elastic.Serilog.Sinks`, is not used at runtime at all.

The chart's Azure Key Vault integration exists only to supply two Elastic secrets. Without Elastic it would deliver nothing, so it goes too.

## What Changes

- **App: remove Elastic APM.**
  - Drop the `services.AddElasticApm()` registration.
  - Drop the `ElasticApm` section from `appsettings.json`.
  - Drop the `Elastic.Apm.NetCoreAll` and `Elastic.Apm.SerilogEnricher` packages.
- **App: remove Elasticsearch logging.** Drop the `Serilog.Sinks.Elasticsearch` and `Elastic.Serilog.Sinks` packages.
- **Chart: remove the Elastic wiring.**
  - Remove the `elastic.*` values.
  - Stop emitting the `ElasticApm__*` and `Serilog__WriteTo__1__*` environment variables.
- **Chart: remove the Key Vault / Secrets Store CSI integration.**
  - Delete `templates/secretprovider.yaml`.
  - Remove the `keyvault.*` values.
  - Remove the secret-backed environment variables, and the CSI volume and its mount.
  - The chart no longer needs the Secrets Store CSI driver in the cluster.
- **BREAKING (environment-variable contract):** `ElasticApm__*` and the Elasticsearch `Serilog__WriteTo__1__*` variables no longer have any effect.
  - A configuration that still names the `Elasticsearch` sink or the `WithElasticApmCorrelationInfo` enricher still starts. Serilog skips the entry and writes a self-log line to stderr.
  - This was checked in a throwaway spike against `Serilog.Settings.Configuration` 10.0.1, the version the app uses.
- **BREAKING (chart values):** the `elastic.*` and `keyvault.*` values are removed.
  - The chart has no `values.schema.json`, so by default Helm would silently ignore them. That would leave a deployment that relied on Elastic with no logs in Elasticsearch, no APM data and no error.
  - To prevent that, rendering the chart fails with a message naming the removed values when `elastic.log.enabled`, `elastic.apm.enabled` or `keyvault.enabled` is `true`.
  - Leftover blocks with those flags set to `false` are ignored.
  - Choosing the version bump for this break is part of tagging the release.
- **Docs:** remove Elastic and Key Vault content from `README.md` and `CLAUDE.md`.

These stay the same:
- the HTTP endpoints and port 50001;
- `Settings__TimeSpan` and `Serilog__MinimumLevel`;
- the Console, Debug, Seq and Syslog sinks, and the `WithMachineName` enricher;
- the Dockerfile and the workflows;
- `rollout-bluegreen.yaml`, which has no environment variables or Key Vault mounts.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `service-runtime`: the deployment configuration contract shrinks to `Settings__TimeSpan` and `Serilog__MinimumLevel`. Elastic variables and leftover Elastic sink or enricher configuration have no effect, and they do not stop the service from starting. The chart emits no Elastic or Key Vault configuration, and it refuses to render when a removed feature is turned on.

## Impact

- **Application code** (made by the dotnet-developer agent): `ExampleNet/Program.cs`, `ExampleNet/ExampleNet.csproj` and `ExampleNet/appsettings.json`.
- **Chart:**
  - `helm/values.yaml` and `helm/templates/deployment.yaml` change;
  - `helm/templates/secretprovider.yaml` is deleted;
  - per the repo convention, these chart edits land in the same commit as the app change.
- **Dependencies:**
  - 33 packages leave the resolved graph.
  - None of the remaining 13 packages changes version, because the Elastic packages asked only for older versions of the packages they share with the app.
- **Docs:** `README.md` and `CLAUDE.md`.
- **Running deployments:**
  - Any environment that turned on Elastic logging or APM stops sending to Elastic once it runs a chart or image built from this change.
  - The chart no longer reads the Key Vault secrets `elasticapm-apikey` and `serilog-writeto-1-args-connectionglobalheaders`, or the managed identity configured for them.
  - Removing those Azure resources is out of scope.
- **Released artifacts:** none change. Charts and images already published, up to and including `1.1.0`, keep Elastic. The `v1.1.0` tag is at `c2f5226`, the commit before this change.
- **Out of scope:**
  - the other logging packages (Seq, Syslog, Debug, Compact formatter);
  - the unused `Settings.ShutdownTimeout`;
  - the startup logging of all configuration values;
  - bringing `rollout-bluegreen.yaml` back in line with `deployment.yaml`.
