## Why

Any tag that starts with `v` starts a release, and this project never wants release candidates. Today `v1.3.2-rc.1` runs the whole release: it publishes and signs an image and a chart, and the bot commits the candidate version to `main`. A manual Build-Docker run from `main` is worse: it pushes and signs an image tagged `main`, then fails chart lint, which leaves a stray image with no chart.

## What Changes

- Add a first job to Build-Docker that fails unless the tag has the form `vMAJOR.MINOR.PATCH`, for example `v1.3.2`. Tests and every later job wait for it, so a rejected tag publishes nothing and fails in seconds.
- **BREAKING**: Remove the manual trigger from Build-Docker. A release can then start only from a pushed tag. **Re-run failed jobs** should still cover a one-off failure.
- Remove the two image tag rules for branch and pull request runs from Build-Docker. Neither kind of run can happen once the workflow starts only from tags.
- State the tag format in `CLAUDE.md` and `README.md`.

Out of scope:

- Published image tags don't change: a release still publishes `X.Y.Z`, `X.Y` and `latest`.
- Chart-only releases are not being added.
- The manual triggers on Build-Helm, Build-Dotnet and Check-Docker stay. None of them can publish anything.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that a release run checks its tag format first and that only a pushed tag starts a release. Changes the definition of a release run in "Releases wait for passing tests" and "Only release runs publish a chart", which both count a manual run of the release workflow today.

## Impact

- `.github/workflows/build-docker.yaml`: new first job, `Test` waits for it, manual trigger removed, two tag rules removed.
- `CLAUDE.md` and `README.md`: release instructions.
- `openspec/specs/ci-test-gate/spec.md`: updated when this change is archived.
- Build-Helm needs no change. A rejected tag makes the Build-Docker run fail, and Build-Helm already skips its chart checks and publishing after a failed Build-Docker run.
- No existing tag is affected. All seven (`v1.0.0` to `v1.3.1`) have the required form.
