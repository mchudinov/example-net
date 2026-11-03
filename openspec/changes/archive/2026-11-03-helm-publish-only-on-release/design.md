## Context

See proposal.md (Why) for the motivation. This is the current state that shapes the approach.

**`Build-Helm` (`.github/workflows/build-helm.yaml`)** has four triggers:
- `workflow_dispatch`;
- `workflow_run` of `Build-Docker` (`types: [completed]`);
- `push` to `main` with `paths: ['helm/**']`;
- `pull_request` to `main` with the same `paths`.

It has three jobs:

```
Check-Helm  --needs-->  Build-and-Push-Helm  --needs-->  Sign-Helm-Chart
                                                          (also needs Check-Helm)
```

- **`Check-Helm`.** Its `if:` skips it only for a `workflow_run` whose conclusion isn't `success` (added by `migrate-tests-to-mstest-sdk`, D7). It lints the chart, installs it into a k3d cluster (dry-run, then a real install of the packaged chart) and runs the Polaris audit. Its outputs are the chart name and version read from `helm/Chart.yaml`, plus the lowercase registry.
- **`Build-and-Push-Helm`.** It has no event condition, and it requests `packages: write`. It runs `helm push` of `<name>-<version>.tgz` to `oci://ghcr.io/<owner>`.
- **`Sign-Helm-Chart`.** It has no event condition either, and it requests `id-token: write` and `packages: write`. It runs `cosign sign` on `<registry>/<name>:<version>`, then `cosign verify`.

GHCR tags are mutable, so `helm push` of an existing version replaces it.

**Release path.** A `v*` tag starts `Build-Docker`, whose `Update-Chart-Version` job commits the bumped `Chart.yaml` to `main` as `github-actions[bot]` using `GITHUB_TOKEN`. GitHub starts no workflows for pushes made with `GITHUB_TOKEN`, so that commit doesn't start a `push` run of `Build-Helm`. The chart is published only through `workflow_run`. Observed for `v1.0.1`: `Build-Docker` run 41640417663, then `Build-Helm` run 41640709665.

**Workflow file used by each trigger.** A `workflow_run` run uses the workflow file from the default branch. A `push` run uses the file at the pushed commit.

## Goals / Non-Goals

**Goals:**
- The publish and sign jobs run only on `workflow_run`. Every other trigger still runs `Check-Helm` in full (see specs/ci-test-gate).
- The release path keeps the same jobs, steps and outputs as today.

**Non-Goals:**
- Detecting or refusing an existing chart version in GHCR (alternative B in D2).
- Changing `Build-Helm`'s triggers or `Check-Helm`'s steps.
- Changing how `Build-Docker` versions a manual run from a branch.
- Rewording the "Unrelated change does not trigger a run" scenario.
- Pinning `setup-polaris@master` and `cosign-installer@main`, or replacing `AbsaOSS/k3d-action`.

## Decisions

### D1. Gate publishing on `github.event_name == 'workflow_run'` in `Build-and-Push-Helm` (planning default)

- Add `if: github.event_name == 'workflow_run'` to `Build-and-Push-Helm` only.
- **`Check-Helm` already covers failed releases.** It is skipped for a `workflow_run` that didn't succeed, and a job whose `needs` was skipped is skipped too. So the new condition only has to exclude the other three events.
- **`Sign-Helm-Chart` skips through `needs`.** It needs `Build-and-Push-Helm` and has no `if:` of its own, so the default `success()` check skips it whenever the push job is skipped. Signing without publishing would be meaningless: it signs the existing tag again.
- **Alternatives:**
  - The same `if:` on `Sign-Helm-Chart` too. This is more explicit, but it duplicates the condition. It would only matter if someone later removed `Build-and-Push-Helm` from `Sign-Helm-Chart`'s `needs`, which would break the publish-then-sign order anyway.
  - Splitting into two workflows, check-only and release. That's cleaner in principle, but it duplicates the check steps and changes the workflow name that release runs show. Not worth it for one condition.
  - A workflow-level `if:` isn't possible. GitHub supports `if:` only on jobs and steps.

### D2. Manual runs only check the chart (agreed: option A)

- `workflow_dispatch` falls under D1's condition, so a manual run executes only `Check-Helm`.
- **Recovery when a release's chart publish fails:** fix the cause and push the next patch tag. The failed version then has an image but no chart. That happened in effect with `v1.0.0` and `v1.0.1`: `1.0.0` was published only by a manual run, which this change no longer allows.
- **Alternative B, rejected:** a manual run publishes only if `helm show chart oci://… --version <v>` finds no existing version. That keeps chart and image version numbers free of gaps, but it adds a registry login and lookup to the check path, and it keeps a second way to publish besides a tag.

### D3. Keep the `push` and `pull_request` triggers as check-only runs (agreed)

- **Why keep them.** A chart change on `main` gets lint, k3d install and Polaris feedback when it is pushed, not only at the next tag. That matters most for Polaris, which fails the release if the score drops below 90.
- **Alternative, rejected:** remove the `push` and `pull_request` triggers. Releases would then be the first place a broken chart shows up.
- **Side effect:** pull request runs no longer reach the jobs that request `packages: write` and `id-token: write`.

### D4. Verify with a separate `helm/**`-only push that changes no chart content (planning default)

- Change `helm/build-helm.sh` line `helm lint --strict` to `helm lint . --strict`, in a commit that touches nothing else, and push it **after** the workflow change.
- **Why this file.** `.helmignore` lists `build-helm.sh`, so the packaged chart is unchanged. That push also starts no `Build-Dotnet` run, which closes task 6.3 of the archived `migrate-tests-to-mstest-sdk` change.
- **Why a separate push.** The workflow change alone starts nothing, because neither `.github/workflows/build-helm.yaml` nor `CLAUDE.md` matches `helm/**` or the `Build-Dotnet` paths. The second push then runs the new `build-helm.yaml` from its own commit.

## Risks / Trade-offs

- **[Risk]** `Check-Helm` passes, but `Build-and-Push-Helm` or `Sign-Helm-Chart` still runs on a push because the condition is wrong. → **Mitigation:** actionlint before pushing; the D4 push checks it live, with the run's job list showing both jobs as skipped.
- **[Risk]** The condition is too strict, so the next release doesn't publish. → **Mitigation:** `workflow_run` is the event the release path already uses (observed for `v1.0.1`). The next tag is checked live (tasks).
- **[Trade-off]** Losing the manual publish path means a failed chart publish costs a version number (D2).
- **[Trade-off]** A `helm/**` push to `main` changes the chart source without publishing it, so `main` can be ahead of the latest published chart until the next tag. This is intended: only a tag releases.
- **[Risk]** Someone hand-edits the `Chart.yaml` version expecting it to publish. → **Mitigation:** nothing publishes, by design. `CLAUDE.md` already says the tag drives the version fields, and its `Build-Helm` description will state that only release runs publish.

## Migration Plan

1. Commit the workflow change and `CLAUDE.md` on `main` and push. No workflow starts.
2. Commit the `helm/build-helm.sh` fix on its own and push. `Build-Helm` runs `Check-Helm` only, and no `Build-Dotnet` run starts.
3. On the next `v*` tag, `Build-Helm` follows `Build-Docker` and publishes and signs the chart as before.
4. **Rollback:** revert the workflow commit. Already-published chart versions are unaffected either way.
