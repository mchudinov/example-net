## Why

Flux installs a chart from an `OCIRepository` with the artifact's digest appended to the chart version, for example `1.3.0+662a06b343e7`. `service.yaml`, `serviceaccount.yaml` and `service-preview.yaml` copy `.Chart.Version` into a `chart:` label as is. `+` isn't allowed in a Kubernetes label value, so the install fails on the ServiceAccount and the Service.

CI doesn't notice, because no chart check uses a version containing `+`.

## What Changes

- **Templates:** delete the `chart:` label from `service.yaml`, `serviceaccount.yaml` and `service-preview.yaml`. Each already includes `app.labels`, which sets `helm.sh/chart` through the `app.chart` helper with `+` replaced by `_`. After this, `.Chart.Version` reaches a label only through that helper.
- **`Build-Helm` checks:** every run packages the chart with a version containing `+`, and then:
  - renders that package with value sets that together reach every template that sets labels, and fails on any invalid label value;
  - installs that package on k3d in its own namespace.
- **`Build-Helm` triggers:** a change to the workflow or to the new check script also runs the chart checks, so a broken check is found before a release.
- **Release 1.3.1:** a `v1.3.1` tag, pushed after `Build-Helm` is green on `main`, publishes image and chart 1.3.1. The release's bot sets both version fields in `helm/Chart.yaml`, as in every earlier release. `Chart.yaml` isn't edited by hand.
- **`CLAUDE.md`:** the Build-Helm section lists the new checks and triggers.
- **BREAKING for anything outside this repo that selects on the `chart` label:** the label disappears from the ServiceAccount and the Services. Nothing in this repo reads it, and its value contained the version, so a selector on it only ever matched one chart version.

**Out of scope:**
- Aligning object names, `app.kubernetes.io/name` or the version flow with the EuGo-docint chart. This change aligns the label path only.
- Adding a build-metadata check to EuGo-docint. Its chart has no raw label, but its CI has no `+` case either.
- Replacing published charts. Chart 1.3.0 keeps the raw labels, so it stays uninstallable through Flux.
- Moving a Flux `OCIRepository` to 1.3.1, which belongs to whoever runs the cluster.
- Making the chart's test install wait for its pods, or installing Argo Rollouts CRDs on k3d. The first keeps the earlier "no smoke tests" decision. The render check makes the second unnecessary.
- The existing drift between `rollout-bluegreen.yaml` and `deployment.yaml`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `service-runtime`: adds a requirement that the chart renders valid labels and installs when its version contains build metadata.
- `ci-test-gate`: adds a requirement that the chart checks cover a version containing build metadata, and fail if a template puts an invalid version in a label.

## Impact

- **Chart:** three templates under `helm/templates/`. `_helpers.tpl`, `values.yaml` and the `Chart.yaml` version fields aren't edited.
- **CI:** `.github/workflows/build-helm.yaml`, plus a new script, `.github/scripts/check-chart-labels.sh`.
- **Docs:** `CLAUDE.md`. `README.md` doesn't mention the label.
- **Release:** tag `v1.3.1` runs `Build-Docker`, then `Build-Helm` publishes. It ships a new image as well as the chart, because the tag drives both.
- **Unchanged:** the application code, the tests, `Dockerfile` and `Build-Docker`. With no C# or project file changes, all the work stays in the main session.
- **When it takes effect:** for Flux, once its source points at chart 1.3.1. The docs say a new chart version resets a stalled HelmRelease's failure counters and retries.
