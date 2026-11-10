## 1. Workflow

- [x] 1.1 In `.github/workflows/build-docker.yaml`, add `Check-Tag` as the first job, with `permissions: {}`, no checkout, and an inline script that matches `GITHUB_REF` against the pattern in `design.md` and on a mismatch prints an `::error::` naming the tag and the required form, then exits 1. Verify by reading the job and by task 3.1.
- [x] 1.2 Add `needs: Check-Tag` to the `Test` job. Verify by reading that every later job still chains back to `Test` through `needs`, so all of them skip when `Check-Tag` fails.
- [x] 1.3 Remove `workflow_dispatch:` from Build-Docker's `on:` block. Verify by reading that a push of `v*` tags is the only trigger left, and that the manual triggers in `build-helm.yaml`, `build-dotnet.yaml` and `check-docker.yaml` are unchanged.
- [x] 1.4 Remove the `type=ref,event=branch` and `type=ref,event=pr` lines from the metadata step. Verify by reading that the two `type=semver` lines remain and that no `flavor` setting was added, so `latest` keeps being published.

## 2. Documentation

- [x] 2.1 In `CLAUDE.md`, update item 3 of the CI/CD section: `Check-Tag` runs first, only `vX.Y.Z` tags are accepted, release candidates are rejected, and Build-Docker has no manual trigger. Verify by reading that the item matches the workflow and that the Build-Helm notes on manual runs are unchanged.
- [x] 2.2 In `README.md`, replace the line saying a tag must start with `v` with the required form, stating that release candidates are rejected. Verify by reading the GitHub actions section.

## 3. Local checks

- [x] 3.1 Extract the check's script from the workflow file rather than retyping it, and run it with `GITHUB_REF` set to each case. It must pass for `refs/tags/v1.3.2`, `refs/tags/v0.0.0` and `refs/tags/v10.20.30`. It must fail, and print the tag and the required form, for `refs/tags/v1.3.2-rc.1`, `refs/tags/v1.3.2+build.1`, `refs/tags/v1.3`, `refs/tags/v1.3.2.1`, `refs/tags/v01.3.2`, `refs/tags/v1.03.2`, `refs/tags/vnext`, `refs/tags/vtag-check-test`, `refs/heads/v1.3.2` and `refs/heads/main`. Result: all 13 cases behaved as required, and all 7 existing release tags pass.
- [x] 3.2 Confirm that `.github/workflows/build-docker.yaml` still parses as YAML, and that `openspec validate enforce-release-tag-format` passes. Result: parsed with the `yaml` package bundled with OpenSpec, and strict validation passed.

## 4. Real run

- [x] 4.1 Ask before pushing, then commit and push the workflow and documentation changes to `main`. Verify that no workflow run starts for the commit. Result: commit `907e018`, and no run started for the push to `main`.
- [x] 4.2 Ask before pushing, then push the tag `vtag-check-test` on that commit. Verify that the Build-Docker run ends as failed, that `Check-Tag` failed with the error naming the tag, and that `Test`, `Build-and-Push-Docker`, `Sign-Docker-Image` and `Update-Chart-Version` were skipped. Result: run 42709392241 failed. `Check-Tag` failed after 3 seconds with `refs/tags/vtag-check-test is not a release tag`, and the four later jobs were skipped.
- [x] 4.3 For the same tag, verify that the Build-Helm run that follows has all its jobs skipped, that GHCR has no new image or chart tag, and that the bot made no commit to `main`. Result: Build-Helm run 42709418589 had all three jobs skipped, GHCR's image and chart tags were unchanged, and `main` stayed at `907e018`.
- [x] 4.4 Delete `vtag-check-test` locally and on GitHub. Verify with `git ls-remote --tags origin` that only release tags remain. Result: only `v1.0.0` to `v1.3.1` remain, and deleting the tag started no run.
- [x] 4.5 Record in this file that the path for a valid tag gets its first full run at the next release. Recorded: valid tags passed the check locally, but no valid tag has passed `Check-Tag` on GitHub or run `Test` behind it yet. The next `vX.Y.Z` release is the first full run.
