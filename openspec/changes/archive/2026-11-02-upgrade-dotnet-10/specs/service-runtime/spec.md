## Purpose

This spec defines what the example-net service guarantees to the systems that build, scan and deploy it:
- a supported runtime;
- a dependency graph free of known vulnerabilities;
- stable HTTP endpoints;
- a stable environment-variable configuration contract;
- a container that runs under the deployment's restricted security context.

## ADDED Requirements

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

### Requirement: Deployment configuration contract is preserved
The service SHALL accept configuration through the environment variable names the Helm chart emits, with unchanged effect:
- `Settings__TimeSpan` sets the interval, in seconds, of the periodic log entries.
- `Serilog__MinimumLevel` sets the minimum level of log entries written.
- `ElasticApm__Enabled`, `ElasticApm__ServerUrls`, `ElasticApm__ServiceName`, `ElasticApm__Environment`, `ElasticApm__MetricsInterval` and `ElasticApm__ApiKey` configure application performance monitoring.
- `Serilog__WriteTo__1__Name=Elasticsearch` enables log delivery to Elasticsearch. It is configured by `Serilog__WriteTo__1__Args__nodeUris`, `Serilog__WriteTo__1__Args__indexFormat` and `Serilog__WriteTo__1__Args__connectionGlobalHeaders`.

#### Scenario: Periodic log interval is configurable
- **WHEN** the service runs with `Settings__TimeSpan=1`
- **THEN** periodic log entries are written about once per second

#### Scenario: Minimum log level is configurable
- **WHEN** the service runs with `Serilog__MinimumLevel=Warning`
- **THEN** no periodic log entries below Warning level are written

#### Scenario: Elasticsearch log delivery is enabled by environment variables
- **WHEN** the service runs with `Serilog__WriteTo__1__Name=Elasticsearch` and `Serilog__WriteTo__1__Args__nodeUris` pointing at a given address
- **THEN** the service attempts to deliver logs to that address, and its diagnostic output does not report that the Elasticsearch sink configuration could not be bound

#### Scenario: Monitoring is enabled by environment variables
- **WHEN** the service runs with `ElasticApm__Enabled=true` and `ElasticApm__ServerUrls` pointing at a given address
- **THEN** the service sends monitoring requests to that address

#### Scenario: Monitoring is off by default
- **WHEN** the service runs with the default configuration, where monitoring is disabled
- **THEN** the service sends no monitoring requests

### Requirement: Container runs under a restricted security context
The container image SHALL start and serve its endpoints when run as a non-root user (UID/GID 1000) with a read-only root filesystem. This matches the security context the Helm chart applies.

#### Scenario: Read-only, non-root container is healthy
- **WHEN** the container image runs as user 1000:1000 with a read-only root filesystem and port 50001 published
- **THEN** `GET /healthz` on the published port returns status 200
