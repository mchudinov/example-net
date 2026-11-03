## REMOVED Requirements

### Requirement: Deployment configuration contract is preserved
**Reason**: The service no longer integrates with Elastic. The `ElasticApm__*` variables and the Elasticsearch `Serilog__WriteTo__1__*` variables were part of this contract and now have no effect. The rest of the contract is carried forward unchanged in "Deployment configuration contract".

**Migration**:
- Remove the `elastic.*` and `keyvault.*` chart values from deployment values. While `elastic.log.enabled`, `elastic.apm.enabled` or `keyvault.enabled` is `true`, rendering the chart fails with a message naming the removed values.
- Environment variables set outside the chart, such as `ElasticApm__*`, can be removed at any time. The service ignores them.
- `Settings__TimeSpan` and `Serilog__MinimumLevel` keep working as before.

## ADDED Requirements

### Requirement: Deployment configuration contract
The service SHALL accept configuration through the environment variable names the Helm chart emits, with unchanged effect:
- `Settings__TimeSpan` sets the interval, in seconds, of the periodic log entries.
- `Serilog__MinimumLevel` sets the minimum level of log entries written.

The service SHALL NOT send monitoring data or logs to Elastic. Leftover Elastic configuration SHALL have no effect and SHALL NOT stop the service from starting. That includes:
- `ElasticApm__*` variables;
- a log sink entry named `Elasticsearch`;
- a log enricher entry named `WithElasticApmCorrelationInfo`.

The Helm chart SHALL NOT emit Elastic configuration. It SHALL NOT require Azure Key Vault or the Secrets Store CSI driver.

If a deployer turns on a removed chart feature, rendering the chart SHALL fail with a message that names the removed values. The removed features are `elastic.log.enabled`, `elastic.apm.enabled` and `keyvault.enabled`, each set to `true`. A deployer who relied on these features is therefore never left with a deployment that silently stops sending data. Removed values set to `false` SHALL be ignored.

#### Scenario: Periodic log interval is configurable
- **WHEN** the service runs with `Settings__TimeSpan=1`
- **THEN** periodic log entries are written about once per second

#### Scenario: Minimum log level is configurable
- **WHEN** the service runs with `Serilog__MinimumLevel=Warning`
- **THEN** no periodic log entries below Warning level are written

#### Scenario: Monitoring variables have no effect
- **WHEN** the service runs with `ElasticApm__Enabled=true` and `ElasticApm__ServerUrls` pointing at a given address
- **THEN** the service sends no requests to that address

#### Scenario: Leftover Elastic logging configuration does not stop the service
- **WHEN** the service runs with these settings:
  - `Serilog__WriteTo__1__Name=Elasticsearch`;
  - `Serilog__WriteTo__1__Args__nodeUris` pointing at a given address;
  - `Serilog__Enrich__2=WithElasticApmCorrelationInfo`.
- **THEN** startup completes, `GET /healthz` returns status 200, and the service sends no requests to that address

#### Scenario: Chart renders no Elastic or Key Vault configuration
- **WHEN** the chart is rendered with its default values, or with `elastic.log.enabled=false`, `elastic.apm.enabled=false` and `keyvault.enabled=false` set
- **THEN** rendering succeeds, and the rendered manifests contain no `ElasticApm__*` or `Serilog__WriteTo__1__*` environment variables, no `SecretProviderClass`, and no Secrets Store CSI volume

#### Scenario: Turning on a removed chart feature fails rendering
- **WHEN** the chart is rendered with any one of `elastic.log.enabled=true`, `elastic.apm.enabled=true` or `keyvault.enabled=true`, with or without `rollout.enabled=true`
- **THEN** rendering fails, and the error message names the removed values
