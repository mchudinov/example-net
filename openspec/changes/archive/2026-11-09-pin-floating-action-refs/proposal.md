## Why

Three `uses:` lines in the release workflows name a branch (`sigstore/cosign-installer@main` twice, `setup-polaris@master` once), so the code they run can change with no commit in this repo. The cosign installer runs in the two signing jobs, which hold `id-token: write` and `packages: write`, and the repo released three times between 2026-11-05 and 2026-11-09, so that code runs often.

## What Changes

- **Cosign installer:** `build-docker.yaml:101` and `build-helm.yaml:177` move from `@main` to `@v4.1.3`. That's the commit `main` points at today, so the action doesn't change. The cosign binary stays at that release's default, v3.1.4, until someone bumps the pin.
- **Polaris installer:** `build-helm.yaml:120` moves from `@master` to `@v10.2.6`, with a comment saying the ref is separate from `POLARIS_VERSION`. The action's folder at that tag is what `master` runs today. `POLARIS_VERSION` stays `8.5.5`, so the audit uses the same binary.
- **`CLAUDE.md`:** the CI section states that third-party actions are never referenced by a branch, and that the `setup-polaris` ref and `POLARIS_VERSION` are independent.

**Out of scope:**
- Files fetched from a `main` branch while a job runs: the two `signing_config.v0.2.json` curls, and the `install.sh` that `AbsaOSS/k3d-action@v2` downloads. These edits wouldn't have prevented the k3d failure in run 41637458630.
- The Polaris binary download, which doesn't check the published `checksums.txt`.
- Moving major-version tags such as `actions/checkout@v7` or `docker/login-action@v4` to exact versions or SHAs.
- Upgrading the Polaris binary from 8.5.5.
- Adding Dependabot, or a CI check that rejects branch references.
- Moving example-net to a shared workflow. If signing moves with it, these pins have to move too.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that workflows never reference a third-party action by a branch, and that the action installing the signing tool is pinned to a version that can't change.

## Impact

- **CI:** three `uses:` lines and one comment across `.github/workflows/build-docker.yaml` and `.github/workflows/build-helm.yaml`.
- **Docs:** `CLAUDE.md`.
- **Unchanged:** the application code, the tests, `Dockerfile`, the chart, and every other workflow step. With no C# or project file changes, all the work stays in the main session.
- **When it takes effect:** the Polaris pin runs on the push to `main`, because `Build-Helm` triggers on its own workflow file. The cosign pins only run in a release, so the next `v*` tag is their first real run.
