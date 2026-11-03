## Why

Only a pushed `v*` tag should create a release. The tag name becomes the version of both the container image and the Helm chart. `Build-Helm` breaks that rule:

- It also runs on pushes to `main` and on pull requests that touch `helm/**`, and on manual runs.
- On those triggers it still packages, pushes and signs the chart under the version currently in `helm/Chart.yaml`.

Only a release changes that version (CLAUDE.md: the tag drives the `Chart.yaml` version fields). So every hand-made chart change between tags overwrites the chart already released in GHCR (now `1.0.1`) with different content, and signs the new content again. A same-repository pull request would publish unmerged chart content the same way.

This hasn't happened yet only because the only `helm/**` pushes so far were the release bot's `Chart.yaml` commits. GitHub starts no workflows for those. The next hand-made chart fix, such as bringing `rollout-bluegreen.yaml` back in line with `deployment.yaml`, would overwrite `1.0.1`.

## What Changes

- **`Build-Helm` publishes only as part of a release.** Packaging, pushing and signing the chart run only when `Build-Helm` follows a successful `Build-Docker` run (`workflow_run`). This is unchanged from today's release path.
- **Every other trigger only checks the chart.** Pushes to `main` and pull requests that touch `helm/**`, and manual runs (`workflow_dispatch`), still run the full check: `helm lint . --strict`, a dry-run and a real install into a k3d cluster, and the Polaris audit. They no longer publish or sign anything.
- **BREAKING (maintainer workflow):** a manual run of `Build-Helm` can no longer publish a chart. That was the recovery path used for chart `1.0.0` after the k3d fix. If a release's chart publishing fails, fix the cause and push the next patch tag. The failed version gets an image but never a chart.
- Update the `Build-Helm` description in `CLAUDE.md`.
- **First live check:** `helm/build-helm.sh` runs `helm lint --strict` without a path, which fails under Helm 4 (CLAUDE.md notes this). Change it to `helm lint . --strict`, and push that fix on its own after the workflow change. The push touches only `helm/**`, and `.helmignore` excludes `build-helm.sh` from the chart package. It therefore checks the new behaviour without changing chart content.

No changes to the chart templates, the chart's values, the container image, the application, or the `Build-Docker` and `Build-Dotnet` workflows.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that a chart version is published only by a release run. Chart changes pushed or proposed to `main`, and manual runs, are checked but publish nothing.

## Impact

- **CI:** `.github/workflows/build-helm.yaml`. Only when its publish and sign jobs run changes; its triggers and check steps are unchanged.
- **Docs:** `CLAUDE.md`. The CI/CD and versioning section changes, and the Commands note about the `build-helm.sh` lint form is no longer needed once the script is fixed.
- **Helper script:** `helm/build-helm.sh`, lint command only. It is not part of the packaged chart.
- **Released artifacts:** none change. Chart `1.0.1` in GHCR stays as published by the `v1.0.1` release.
- **Follow-on:** once this is in place, a hand-made `helm/**`-only push is safe. The `build-helm.sh` push can then also close task 6.3 of the archived `migrate-tests-to-mstest-sdk` change, which checks that such a push starts no `Build-Dotnet` run.
- **Out of scope:**
  - A manual run of `Build-Docker` from a branch takes its version from the branch name (for example `main`).
  - Rewording the "Unrelated change does not trigger a run" scenario to cover pushes as well as pull requests.
