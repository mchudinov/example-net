## ADDED Requirements

### Requirement: Chart's default image is the image releases publish
Rendered with its default values, the Helm chart SHALL reference the container image at `ghcr.io/mchudinov/example-net-docker`, the location releases push to, tagged with the chart's app version. This SHALL hold for both forms the chart can render: the Deployment and the blue-green Rollout.

A deployer SHALL be able to override the repository and the tag.

#### Scenario: Default render uses the release location
- **WHEN** the chart is rendered with its default values
- **THEN** the application container's image is `ghcr.io/mchudinov/example-net-docker:<app version>`

#### Scenario: Blue-green Rollout uses the release location
- **WHEN** the chart is rendered with `rollout.enabled=true`
- **THEN** the application container's image is `ghcr.io/mchudinov/example-net-docker:<app version>`

#### Scenario: Deployer overrides the image
- **WHEN** the chart is rendered with `image.repository` and `image.tag` set
- **THEN** the application container's image uses that repository and tag

### Requirement: Image and chart can be pulled without credentials
The container image and the Helm chart that releases publish SHALL be readable without credentials. The chart's default values SHALL NOT reference a pull secret. A deployer SHALL still be able to supply one through `imagePullSecrets`.

#### Scenario: Image is readable without credentials
- **WHEN** the manifest of a released image version tag is requested with no registry credentials
- **THEN** the request succeeds

#### Scenario: Chart is readable without credentials
- **WHEN** a released chart version is pulled with no registry credentials
- **THEN** the pull succeeds

#### Scenario: Default render references no pull secret
- **WHEN** the chart is rendered with its default values, with or without `rollout.enabled=true`
- **THEN** the rendered manifests contain no `imagePullSecrets`

#### Scenario: Deployer-supplied pull secret is used
- **WHEN** the chart is rendered with `imagePullSecrets` naming a secret, with or without `rollout.enabled=true`
- **THEN** the rendered pod spec references that secret
