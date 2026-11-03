## Context

See proposal.md for why. The current wiring has three layers:

```
 values.yaml                deployment.yaml env                 app
 elastic.apm.*   ------->   ElasticApm__*              ----->   services.AddElasticApm()
 elastic.log.*   ------->   Serilog__WriteTo__1__*     ----->   ReadFrom.Configuration -> Elasticsearch sink
 keyvault.*      --+        2nd ElasticApm__ApiKey and
                   |        connectionGlobalHeaders from k8s secrets (the later entry wins)
                   v                  ^
         secretprovider.yaml ---------+   syncs only elasticapm-apikey and
         (Azure Secrets Store CSI)        serilog-writeto-1-args-connectionglobalheaders
```

Where Elastic appears in the app:
- One line of code: `Program.cs:64`, `services.AddElasticApm();`. There are no Elastic `using`s.
- Four package references in `ExampleNet.csproj`:
  - `Elastic.Apm.NetCoreAll`
  - `Elastic.Apm.SerilogEnricher`
  - `Elastic.Serilog.Sinks`
  - `Serilog.Sinks.Elasticsearch`
- One config section: `ElasticApm` in `appsettings.json`.

Where it appears in the chart:
- `values.yaml`: the `elastic.*` and `keyvault.*` values;
- `templates/deployment.yaml`: the env blocks and the CSI `volumeMounts:` and `volumes:`;
- `templates/secretprovider.yaml`: all of it.

`rollout-bluegreen.yaml` has none of this. `deployment.yaml` renders only when `rollout.enabled` is false.

What was verified before this design:
- **Package graph** (from `project.assets.json`): the four Elastic references bring in 33 of the 46 resolved packages. Where they share a package with the app, they ask only for older versions, for example `Serilog >= 3.1.1` while 4.4.0 resolves. The remaining 13 packages keep their versions.
- **Leftover config** (throwaway spike against `Serilog.Settings.Configuration` 10.0.1, outside the repo):
  - The test config had a `WriteTo` entry named `Elasticsearch` and an `Enrich` entry named `WithElasticApmCorrelationInfo`, with neither assembly present.
  - Serilog wrote `Unable to find a method called ...` to its self-log (stderr) and built the logger. The process kept running.
- **Render guard** (throwaway chart under Helm v4.1.4, outside the repo):
  - The test was a template with no manifest: `dig` over `.Values.AsMap`, then `fail`.
  - `helm lint . --strict` passed with default values.
  - `helm template` succeeded with defaults and with the flags explicitly `false`.
  - `helm template` failed with the guard's message for each flag set to `true`, given by `--set` or a values file, including with `rollout.enabled=true`.
  - `helm lint` does not fail on `fail`: it logs the message at INFO level and passes. So the guard is tested with `helm template`, not `helm lint`.
- **OpenSpec 1.12.0:**
  - Archive rejects a MODIFIED delta whose block drops scenarios that are still in the main spec.
  - It also rejects REMOVED and ADDED under the same requirement name.

## Goals / Non-Goals

**Goals:**
- Leave no Elastic or Key Vault code, packages, config, chart values, templates or docs in the repo.
- Make the chart break loud for anyone who still turns on a removed feature.
- Keep the remaining configuration contract and the HTTP behaviour exactly as they are.

**Non-Goals:**
- Replacing Elastic with another log or APM backend.
- Removing Elastic Azure resources: the Key Vault secrets and the managed identity.
- Any cleanup beyond Elastic and Key Vault, such as the other Serilog sinks, the startup config logging, `ShutdownTimeout` or `rollout-bluegreen.yaml` drift.

## Decisions

### D1. Remove all four Elastic package references, including the ones nothing uses

`Elastic.Serilog.Sinks` is never called at runtime. `Elastic.Apm.SerilogEnricher` is used only by README examples. Both still go, so the dependency graph contains no Elastic packages at all.

Alternative considered: keep `Serilog.Sinks.Elasticsearch`, so operators could still opt in through configuration. Rejected: the maintainer's decision is to remove Elastic, and an opt-in sink would keep the configuration surface and its transitive packages.

### D2. Remove Key Vault and the Secrets Store CSI integration entirely

After D1, `secretprovider.yaml` would have no objects to sync. Keeping it would leave a `SecretProviderClass` that delivers nothing, while still requiring the CSI driver and a managed identity in the cluster.

Alternative considered: keep it as a reference pattern with an empty or placeholder object list. Rejected by the maintainer. A future Key Vault example should be built around a real secret.

When the `keyvault` blocks go, the `volumeMounts:` and `volumes:` keys in `deployment.yaml` go too. Each held only the CSI entry, and leaving them would render empty keys.

### D3. Fail rendering when a removed feature is turned on (maintainer's choice)

A guard calls Helm's `fail` when `elastic.log.enabled`, `elastic.apm.enabled` or `keyvault.enabled` is true. The message names the removed values and says they must be removed.

How the guard works:
- **Placement.** It lives in its own template, for example `templates/removed-values.yaml`. That file renders no manifest, only the checks. A guard inside `deployment.yaml` would be skipped when `rollout.enabled=true`, because `deployment.yaml` is wrapped in `if not .Values.rollout.enabled`.
- **Value lookup.** It reads the values with `dig` over `.Values.AsMap` and a default of `false`. Plain dot access would fail with a nil-pointer error once `values.yaml` no longer defines `elastic` or `keyvault`.
- **Disabled leftovers.** It checks only the `enabled` flags. Leftover blocks with `enabled: false` stay harmless, so values files that merely still contain the old keys keep working.

Alternatives considered:
- Ignore the values silently. This is the smallest change, but a deployment relying on Elastic would lose its logs and APM data with no signal.
- Add a `values.schema.json` that rejects the keys. This is stricter: it would also reject harmless disabled leftovers. Introducing a schema is also a larger change than the problem needs.

The guard can be dropped in a later release once deployers have migrated.

### D4. Replace the spec requirement instead of modifying it

The Elastic scenarios must leave `service-runtime`, but OpenSpec refuses a MODIFIED delta that drops scenarios (see Context). So the delta REMOVES "Deployment configuration contract is preserved" and ADDS "Deployment configuration contract":
- The new requirement carries over the two surviving scenarios, word for word.
- It adds scenarios that make the removal testable: monitoring variables send nothing, leftover log config doesn't stop startup, and the chart renders or refuses as described in D3.

Requirement order in the main spec changes, since ADDED requirements are appended. That is acceptable.

### D5. One commit on `main` for code and chart, made by the dotnet-developer agent

Per the repo conventions:
- C# files and `appsettings*.json` go to the dotnet-developer agent.
- A chart change that goes with a C# change goes in the same brief, so both land in one commit.
- Work happens on `main`, with no branch or PR.

README, CLAUDE.md and OpenSpec files are edited in the main session.

## Risks / Trade-offs

- **[Risk]** A deployment that set Elastic variables outside the chart, such as a hand-written manifest or `pod-example.yaml`-style usage, gets no render error, and its data stops arriving silently.
  → **Mitigation:** the proposal and the spec's Migration note say so. Serilog's self-log line on stderr names the missing sink or enricher. The variables do no harm otherwise.
- **[Risk]** `Serilog.Settings.Configuration` behaviour for unknown entries could change in a future version. A later version might throw instead of writing to the self-log.
  → **Mitigation:** the "Leftover Elastic logging configuration does not stop the service" scenario is checked during apply, against the real container. A future package bump that breaks it would be caught when that scenario is checked again.
- **[Risk]** Removing 33 packages could expose a version shift or a vulnerability that the Elastic graph was hiding.
  → **Mitigation:** the analysis shows no version shift. The vulnerability audit, including transitive packages, is re-run after removal.
- **[Trade-off]** The D3 guard is extra chart code that exists only for migration. It is kept small, isolated in one file, and marked for later removal.

## Migration Plan

1. Land the app, chart and docs change on `main`.
   - `Build-Helm` runs on the `helm/**` push and checks lint, the k3d install and Polaris.
   - It publishes nothing, because only a release publishes.
2. Release with a new `v*` tag. Choose the version bump for the breaking chart change at tag time.
3. Deployers remove the `elastic.*` and `keyvault.*` values before upgrading. If one of them is still set to `true`, `helm upgrade` fails with the guard's message and the running release stays unchanged.
4. Rollback: install the previous chart version. Published `1.0.x` charts and images still contain Elastic.
