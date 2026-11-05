# service-runtime Specification

## Purpose

This spec defines what the example-net service guarantees to the systems that build, scan and deploy it:
- a supported runtime;
- a dependency graph free of known vulnerabilities;
- stable HTTP endpoints;
- a stable environment-variable configuration contract;
- a container that runs under the deployment's restricted security context.

## Requirements

### Requirement: Supported long-term-support runtime
The service SHALL run on a long-term-support (LTS) release of its runtime. That release SHALL be in active vendor support, with an end-of-support date later than the date the change is validated. This applies both to the application as built and to the runtime shipped in the container image.

#### Scenario: Runtime release is LTS and in active support
- **WHEN** the vendor's release metadata is checked for the runtime version the built application targets
- **THEN** the release type is LTS, the support phase is active, and the end-of-support date is after the validation date

#### Scenario: Container image ships the same supported runtime
- **WHEN** the runtime shipped in the container image is inspected
- **THEN** its version belongs to the same supported LTS release

### Requirement: No known vulnerable dependencies
The service's resolved dependency graph, for both the application and its test project and including transitive dependencies, SHALL contain no package with a known published vulnerability at the time the change is validated.

#### Scenario: Transitive vulnerability audit is clean
- **WHEN** a vulnerability audit that includes transitive dependencies runs against the public advisory database
- **THEN** no vulnerable packages are reported for the application or the test project

### Requirement: HTTP endpoints and port are preserved
The service SHALL listen for HTTP on port 50001 by default. Once startup has completed, it SHALL answer `GET /`, `GET /healthz`, `GET /readyz` and `GET /livez` with status 200. `GET /` SHALL return the body `Ok`.

#### Scenario: Endpoints respond when run directly
- **WHEN** the application runs with its default configuration and startup has completed
- **THEN** `GET /`, `/healthz`, `/readyz` and `/livez` each return status 200, and `GET /` returns the body `Ok`

#### Scenario: Endpoints respond from the container on port 50001
- **WHEN** the container image runs with container port 50001 published
- **THEN** `GET /healthz` on the published port returns status 200

### Requirement: Container runs under a restricted security context
The container image SHALL start and serve its endpoints when run as a non-root user (UID/GID 1000) with a read-only root filesystem. This matches the security context the Helm chart applies.

#### Scenario: Read-only, non-root container is healthy
- **WHEN** the container image runs as user 1000:1000 with a read-only root filesystem and port 50001 published
- **THEN** `GET /healthz` on the published port returns status 200

### Requirement: Container image contains only the application
The container image's application directory SHALL contain only the application's own publish output. It SHALL NOT contain test assemblies, test frameworks, test runners or code-coverage tooling.

#### Scenario: Application directory has no test payload
- **WHEN** the files under the image's application directory (`/App`) are listed
- **THEN** none of them belongs to the test project, a test framework, a test runner or code-coverage tooling. For example, there are no `ExampleNetTests.*`, `MSTest*`, `testhost.*`, `Microsoft.TestPlatform.*`, `Microsoft.Testing.*`, `coverlet.*` or `Microsoft.CodeCoverage` entries

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

### Requirement: Container image is published for amd64 and arm64
Each release SHALL publish the container image for `linux/amd64` and `linux/arm64`. Its version tags SHALL point at one multi-platform image, so a client that pulls a version tag gets the image for its own architecture. That multi-platform image's digest SHALL be signed.

Neither platform's image SHALL contain a binary built specifically for a different architecture. Both SHALL contain the same set of files in the application directory.

#### Scenario: Version tag lists both platforms
- **WHEN** the manifest of a released version tag is inspected
- **THEN** it lists an image for `linux/amd64` and an image for `linux/arm64`

#### Scenario: Signature verifies for the released image
- **WHEN** the release's signature is verified against the digest that a released version tag resolves to
- **THEN** verification succeeds

#### Scenario: Each image contains no binary built for another architecture
- **WHEN** the application directory (`/App`) of each platform's image is inspected without starting the container
- **THEN** it contains no executable or library built specifically for a different architecture

#### Scenario: Both platforms ship the same files
- **WHEN** the file listings of the application directory (`/App`) in the two platforms' images are compared
- **THEN** the listings are identical
