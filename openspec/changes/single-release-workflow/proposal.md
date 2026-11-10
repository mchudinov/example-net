## Why

A release is split over two workflow runs, and the second doesn't build from the tag. Build-Helm starts on `workflow_run`, and such a run always checks out `main`'s newest commit. For v1.3.1, Build-Helm run 42621519820 built the chart from the bot commit `376a2cb`, while the tag commit `0943829` still says `version: 1.3.0` in `helm/Chart.yaml`. Three more problems follow from the split:

- The chart is checked only after the image has been pushed and signed, so a failed chart check leaves a published image with no chart.
- `Build-and-Push-Helm` packages the chart again from a fresh checkout, so the published `.tgz` isn't the file `Check-Helm` checked.
- Anything merged to `main` between the tag push and Build-Helm goes into the chart but not the image. A tag on an older commit, such as a hotfix, gets a chart built from `main`.

The bot commit that sets the chart version also needs `contents: write` and a direct push to `main`, which a branch rule would block.

## What Changes

- **BREAKING**: Rename Build-Docker to **Release** (`build-docker.yaml` becomes `release.yaml`). One run per `v*` tag builds, checks and publishes everything from the tag's commit. Earlier runs stay listed under Build-Docker in the Actions tab.
- The release run checks the chart alongside the tests and before it pushes the image. If the tests or the chart checks fail, it publishes nothing: no image and no chart.
- Build-Helm becomes a reusable workflow that only checks. Release calls it. Its push, pull request and manual triggers stay, so `main` and pull requests keep the same checks. Its `workflow_run` trigger and both publishing jobs are removed.
- The chart is packaged once, at the tag's version. Lint and the test-cluster install run on that package, and Release publishes the same file, handed over as a run artifact.
- The chart's `version` and `appVersion` both come from the tag at package time. The bot commit and the `Update-Chart-Version` job are removed, so a release writes nothing to the repository. The two values in `helm/Chart.yaml` become placeholders that stay at 1.3.1, with a comment saying so.
- Publishing the chart and signing it move into Release and still run only after the image has been signed. The helm registry login now reads its token from the step's environment.
- `CLAUDE.md` and the workflow comments describe the single run, remove the rules that existed only because Build-Helm found Build-Docker by name, and add one rule: never push a release tag on a commit older than this change.

Out of scope:

- Published image tags don't change: a release still publishes `X.Y.Z`, `X.Y` and `latest`.
- The image build (builder, cache, platforms) doesn't change.
- The chart is still signed by tag rather than by digest.
- Files fetched from moving branches at run time, such as k3d's `install.sh` and the sigstore signing config.
- A ruleset that protects `v*` tags or `main`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`:
  - Adds three requirements:
    - a release checks the chart before it pushes the image;
    - the published chart is the package that was checked;
    - the chart's version comes from the release tag.
  - Six requirements describe the bot commit or a separate chart run after the release, and are reworded for the single run:
    - "Releases wait for passing tests"
    - "Only release runs publish a chart"
    - "Changes that affect the container image are checked by an image build"
    - "Chart checks cover a version with build metadata"
    - "Chart checks enforce the audit policy"
    - "Chart checks stop waiting for a test cluster that doesn't come up"
  - "Release tags name an exact version" adds the chart checks to what a rejected tag stops.

## Impact

- `.github/workflows/build-docker.yaml` becomes `.github/workflows/release.yaml`:
  - Check-Tag outputs the version.
  - New jobs: `Check-Helm` (calls Build-Helm), `Push-Helm` and `Sign-Helm-Chart`.
  - `Update-Chart-Version` is deleted. With it go the `contents: write` permission and the `mikefarah/yq` and `planetscale/ghcommit-action` actions.
- `.github/workflows/build-helm.yaml`:
  - Gains `workflow_call` with a `version` input and outputs.
  - Loses `workflow_run`, `Build-and-Push-Helm` and `Sign-Helm-Chart`.
  - Packages once into `$RUNNER_TEMP` and uploads the package.
- New action references: `actions/upload-artifact@v7` in Build-Helm, and `actions/download-artifact@v8` and `azure/setup-helm@v5` in Release's `Push-Helm`.
- `.github/workflows/check-docker.yaml` and `.github/workflows/build-dotnet.yaml`: comments that name Build-Docker.
- `helm/Chart.yaml`: comments only. The values stay at 1.3.1.
- `CLAUDE.md`: the CI/CD section, the "Default image" item and the closing paragraph.
- `openspec/specs/ci-test-gate/spec.md`: updated when this change is archived.
- Nothing outside the repo reads `main`'s `Chart.yaml`. Flux installs from the `OCIRepository` `oci://ghcr.io/mchudinov/example-net-helm` with `semver: "1.x"`.
- Every published image and chart (1.0.0 to 1.3.1) stays as it is.
