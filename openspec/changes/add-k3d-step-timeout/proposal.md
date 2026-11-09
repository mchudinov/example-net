## Why

The step in `Build-Helm` that creates the test k3d cluster has no time limit, and k3d's default is to wait forever for its nodes, so only GitHub's 6-hour job limit stops it. On 2026-11-09 it hung for 2 h 43 min until it was cancelled, against 26 to 47 s normally, and left nothing that explains why. During a release, the same hang would leave the image published and the chart unpublished, while the run just shows as in progress.

## What Changes

- **Time limit:** the k3d step gets `timeout-minutes: 5`. A stuck cluster fails the step, and the run ends as failed.
- **Evidence:** a new step runs only when the k3d step failed. It prints the k3d containers, their status and their logs to the run log, so a repeat of the hang can be diagnosed.
- **Recovery:** `CLAUDE.md` gains **Re-run failed jobs** as the way to recover from a one-off failure in a release's chart run. Pushing the next patch tag stays the way to recover when the cause needs a commit.

**Out of scope:**
- Finding the cause of the 2026-11-09 hang. The new logs are how a repeat would be diagnosed.
- Retrying the cluster automatically. One hang in about 15 runs doesn't justify the duplicated step and cleanup.
- Moving the chart checks ahead of the image push. Only that would prevent a half-finished release. With this change, a hang during a release still leaves the image out without a chart until someone re-runs the failed run.
- Time limits on other steps or jobs. No other hang has been seen.
- Cutting a release. No chart or image content changes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that the chart checks stop waiting for a test cluster that doesn't come up, fail the run, and show the cluster's containers and logs.

## Impact

- **CI:** one edited step and one new step in `.github/workflows/build-helm.yaml`.
- **Docs:** the recovery sentence in `CLAUDE.md`.
- **Unchanged:** the chart, `.github/polaris-config.yaml`, the application code and the tests. With no C# or project file changes, all the work stays in the main session.
- **Run logs:** this repository is public, so the logs of a failed test cluster are public. The cluster is thrown away with the runner, and no repository secret goes into it.
- **History on `main`:** proving the failure path on a real runner adds a temporary commit and its revert.
