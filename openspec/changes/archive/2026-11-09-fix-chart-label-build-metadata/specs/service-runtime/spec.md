## ADDED Requirements

### Requirement: Chart works with build metadata in its version
The Helm chart SHALL be installable when its version contains SemVer build metadata. This is the form Flux gives a chart it installs from an `OCIRepository`: `<version>+<first 12 characters of the digest>`.

With such a version, every label value the chart renders SHALL be a valid Kubernetes label value: empty, or at most 63 characters that begin and end with an alphanumeric character and contain only alphanumerics, `-`, `_` and `.`.

A label that carries the chart version SHALL carry it with `+` replaced by `_`. Every object the chart labels with `helm.sh/chart` SHALL keep that label.

#### Scenario: Default values render valid labels
- **WHEN** the chart, with a version containing build metadata, is rendered with its default values
- **THEN** every label value in the rendered manifests is a valid Kubernetes label value

#### Scenario: Blue-green Rollout renders valid labels
- **WHEN** the chart, with a version containing build metadata, is rendered with `rollout.enabled=true` and `rollout.strategy=bluegreen`
- **THEN** every label value in the rendered manifests is a valid Kubernetes label value

#### Scenario: Chart version label uses the replaced form
- **WHEN** the chart with version `1.3.1+662a06b343e7` is rendered
- **THEN** `helm.sh/chart` is `example-net-helm-1.3.1_662a06b343e7`, and no label value contains `+`

#### Scenario: Every labelled object keeps the chart label
- **WHEN** the chart is rendered with values that turn on its ingress, its autoscaler or its blue-green Rollout with preview ingress
- **THEN** every ServiceAccount, Service, Deployment, Rollout, HorizontalPodAutoscaler, Ingress and test Pod it renders carries `helm.sh/chart`

#### Scenario: No separate chart label
- **WHEN** the chart is rendered with its default values, or with `rollout.enabled=true` and `rollout.strategy=bluegreen`
- **THEN** no rendered object has a label with the key `chart`

#### Scenario: Cluster accepts the chart
- **WHEN** the chart, with a version containing build metadata, is installed with its default values on a Kubernetes cluster
- **THEN** the install succeeds, with every object accepted by the API server
