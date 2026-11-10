## 1. Build-Helm becomes a reusable workflow that only checks

- [x] 1.1 In `.github/workflows/build-helm.yaml`, change the triggers:
  - Add `workflow_call` with a `version` input (`type: string`, `required: false`, `default: ''`).
  - Add outputs `chart_name`, `chart_version`, `chart_file` and `registry_lowercase`, each mapped to `jobs.Check-Helm.outputs.*`.
  - Remove `workflow_run` and the `if` on `Check-Helm`.
  - Keep `workflow_dispatch`, `push` and `pull_request`.

  Verify by reading the `on:` block, and with `git diff` that the push and pull request `paths` lists are unchanged.
- [x] 1.2 Delete the `Build-and-Push-Helm` and `Sign-Helm-Chart` jobs, and the workflow-level `GHCR_USERNAME`, `GHCR_PASSWORD` and `OIDC_ISSUER` variables. Verify that a grep of `build-helm.yaml` finds none of these: `GHCR_`, `OIDC_ISSUER`, `packages:`, `id-token`, `cosign`, `helm push`, `registry login`.
- [x] 1.3 Rewrite the "Extract chart info" step:
  - Read the input through the step's `env` as `INPUT_VERSION: ${{ inputs.version }}`.
  - Set `VERSION` to that input, or, when it is empty, to `sed -n 's/^version: //p' Chart.yaml`.
  - Read the name with `sed -n 's/^name: //p' Chart.yaml`.
  - Write `CHART_VERSION`, `CHART_NAME` and `REGISTRY_LOWERCASE` to `GITHUB_OUTPUT`.
  - Before the fallback, fail with an `::error::` when `GITHUB_EVENT_NAME` is `push`, `GITHUB_REF` starts with `refs/tags/` and `INPUT_VERSION` is empty.

  Verify by running the extracted script locally against the real `helm/Chart.yaml`:
  - `INPUT_VERSION=1.3.2` must give 1.3.2;
  - an empty `INPUT_VERSION` must give 1.3.1 for `push` on `refs/heads/main`, for `pull_request`, and for `workflow_dispatch` on `refs/tags/v1.3.1`;
  - `push` on `refs/tags/v1.3.2` with an empty `INPUT_VERSION` must exit 1 with the error.
- [x] 1.4 Package once:
  - Replace the "Package helm" step, which writes into `helm/`, with a step right after chart info that runs `helm package ./ --version "$VERSION" --app-version "$VERSION" --destination "$RUNNER_TEMP"`.
  - That step writes `CHART_FILE` and `CHART_PACKAGE` to `GITHUB_OUTPUT`.
  - Point "Lint helm" (`helm lint "$CHART_PACKAGE" --strict`), "Dry-run install" and "Test install helm chart" at `$CHART_PACKAGE`, with values passed through `env`.
  - Add a comment on the test install saying it must not use `--wait` or `--atomic`, because a release's image doesn't exist yet at that point.

  Verify by reading that no step packages into `helm/`.
- [x] 1.5 In "Package helm with build metadata in the version", use `"$VERSION+662a06b343e7"` and add `--app-version "$VERSION"`. Remove the comment about building before "Package helm" because of `.helmignore`. Leave the label check, the build-metadata install and Polaris unchanged. Verify by reading.
- [x] 1.6 Add `actions/upload-artifact@v7` as the last step of `Check-Helm`, after "Use Polaris", with:
  - `name: helm-chart`
  - `path` set to the package
  - `if-no-files-found: error`
  - `overwrite: true`
  - no `retention-days`

  Verify by reading that it is the last step.
- [x] 1.7 Set `Check-Helm`'s job outputs to `chart_name`, `chart_version`, `chart_file` and `registry_lowercase`, taken from the chart info and package steps. Verify by reading that each `workflow_call` output resolves to a job output that a step sets.

## 2. Release workflow

- [x] 2.1 Rename the workflow file with `git mv .github/workflows/build-docker.yaml .github/workflows/release.yaml` and set `name: Release`. Verify that `git status` shows a rename and that `on:` is still only `push` of `v*` tags.
- [x] 2.2 In `Check-Tag`, after the regex check, write `version=${GITHUB_REF_NAME#v}` to `GITHUB_OUTPUT`, and declare job output `version`. Keep `permissions: {}`. Verify by running the extracted script locally:
  - `v1.3.2` must give `version=1.3.2`;
  - a malformed tag must exit 1 and write no version.
- [x] 2.3 Add a job `Check-Helm` with:
  - `uses: ./.github/workflows/build-helm.yaml`
  - `needs: Check-Tag`
  - `permissions: contents: read`
  - `with: version: ${{ needs.Check-Tag.outputs.version }}`

  Set `Build-and-Push-Docker` to `needs: [Test, Check-Helm]`. Verify by reading the `needs` lines.
- [x] 2.4 Delete `Update-Chart-Version`, and remove the workflow-level `HELM_WORKING_DIRECTORY` and `BRANCH` variables. Verify that a grep of `release.yaml` finds none of these: `contents: write`, `yq`, `ghcommit`, `Chart.yaml`, `BRANCH`, `HELM_WORKING_DIRECTORY`.
- [x] 2.5 Add `Push-Helm`:
  - `needs: [Check-Helm, Sign-Docker-Image]`
  - `permissions: packages: write`
  - no checkout
  - steps:
    1. `azure/setup-helm@v5`
    2. `actions/download-artifact@v8`, with `name: helm-chart` and `path: ${{ runner.temp }}/helm-chart`
    3. `helm registry login ghcr.io`, with the user and token passed through `env` and the token read as `"$GHCR_PASSWORD"` on stdin
    4. a check that fails with an `::error::` unless `needs.Check-Helm.outputs.chart_version`, passed through `env`, equals `${GITHUB_REF_NAME#v}`
    5. `helm push "$RUNNER_TEMP/helm-chart/$CHART_FILE" "oci://$REGISTRY"`, with both values passed through `env` from `needs.Check-Helm.outputs`

  Verify by reading that no `run:` script in `Push-Helm` contains `${{`. Also run the extracted check locally: it must pass for chart version 1.3.2 with `GITHUB_REF_NAME=v1.3.2`, and fail for 1.3.1 with `v1.3.2`.
- [x] 2.6 Move `Sign-Helm-Chart` from Build-Helm into Release with `needs: [Check-Helm, Push-Helm]`, keeping its permissions and steps. Its `needs.Check-Helm.outputs.*` references now resolve to the called workflow's outputs. Verify by comparing it with the deleted job in `git diff`: only `needs` may differ.
- [x] 2.7 Parse `release.yaml` with the `yaml` package bundled with OpenSpec and print every job's `needs`. Verify that:
  - every job chains back to `Check-Tag`;
  - `Build-and-Push-Docker` needs both `Test` and `Check-Helm`;
  - `Push-Helm` comes after `Sign-Docker-Image`, and `Sign-Helm-Chart` after `Push-Helm`;
  - no job refers to a job that doesn't exist.

## 3. Chart

- [x] 3.1 In `helm/Chart.yaml`, replace the two version comments with one block saying:
  - a release tag `vX.Y.Z` publishes the image and the chart at `X.Y.Z`;
  - CI stamps both values at package time, so these are placeholders;
  - they aren't edited for a release;
  - a local install deploys image 1.3.1 unless `image.tag` is set.

  Leave `version: 1.3.1` and `appVersion: "1.3.1"` as they are. Verify that:
  - `git diff helm/Chart.yaml` shows only comment lines;
  - the block has no `version:` text;
  - `helm lint helm --strict` passes.

## 4. Documentation and comments

- [x] 4.1 Update the CI/CD section of `CLAUDE.md` as listed under "Documentation" in `design.md`:
  - item 2's Build-Docker references;
  - item 3 (Release);
  - item 4 (Build-Helm checks only and is called by Release);
  - the re-run paragraph;
  - the new rule against tagging a commit older than this change;
  - removal of the name rule;
  - the "Default image" item;
  - the closing sentence about the version fields.

  Verify by reading the section against both workflows, and that a grep of `CLAUDE.md` finds no `workflow_run`, bot commit or "don't rename that workflow".
- [x] 4.2 In `check-docker.yaml`:
  - remove the "Never name this workflow Build-Docker" comment;
  - point the builder comment at Release;
  - point "Must match the list in build-docker.yaml" at `release.yaml`.

  In `build-dotnet.yaml`, change the comment to "Release calls this workflow as its release gate". Verify that a grep of `.github/` for `Build-Docker` and `build-docker.yaml` returns nothing.
- [x] 4.3 Read the GitHub actions section of `README.md` and confirm it needs no change, since it still says a tag versions both packages. Record the result here. Result: no change needed. The section says a tag versions both packages and both are pushed to GHCR. It names no workflow and mentions neither the bot commit nor the chart version in `Chart.yaml`.

## 5. Local checks

- [x] 5.1 Parse all four workflows with the `yaml` package and print their triggers. Verify that:
  - Release has only `push` of `v*` tags;
  - Build-Helm has `workflow_dispatch`, `workflow_call`, `push` and `pull_request`;
  - no workflow has `workflow_run`.
- [x] 5.2 List every `uses:` in `.github/workflows/`. Verify that each reference to another repository names a release tag or a full commit SHA, including the new `actions/upload-artifact@v7`, `actions/download-artifact@v8` and `azure/setup-helm@v5`. Result: all 11 references are tags that exist on GitHub, and none is also a branch. `mikefarah/yq` and `planetscale/ghcommit-action` are gone.
- [x] 5.3 Re-run the tag check's 13 cases from the archived `enforce-release-tag-format` tasks against the script extracted from `release.yaml`. Also check that the 7 existing release tags pass and each writes the expected `version`. Record the result here. Result: all 13 cases behaved as required. The 3 valid refs wrote `version=1.3.2`, `0.0.0` and `10.20.30`, and the 10 invalid refs failed with the error and wrote no version. All 7 existing tags passed and wrote their version without the `v`.
- [x] 5.4 Simulate `Check-Helm`'s release path locally in the scratchpad with the extracted scripts and `INPUT_VERSION=1.3.2`:
  - package the chart;
  - `helm lint --strict` the package;
  - `helm show chart` must show `version` and `appVersion` 1.3.2;
  - run the label check on the build-metadata package.

  Repeat with an empty `INPUT_VERSION` and expect 1.3.1. Record the results here. Result, with Helm v4.1.4:
  - `INPUT_VERSION=1.3.2` on a tag push gave `example-net-helm-1.3.2.tgz`. Lint passed, `version` and `appVersion` were 1.3.2, and the default image was `example-net-docker:1.3.2`. The build-metadata package was `1.3.2+662a06b343e7` with `appVersion` 1.3.2, and both label sets passed (32 and 37 values).
  - An empty input on a push to `main` gave the same results at 1.3.1.
  - Neither run left a package in `helm/`.
- [x] 5.5 Run `openspec validate single-release-workflow --strict` and confirm it passes.

## 6. Real runs

- [x] 6.1 Ask before pushing. Optionally commit the archived `enforce-release-tag-format` change first, naming only its paths in `git commit -- <paths>`, so that the staged rename stays out of it. Check each commit with `git show --stat HEAD`. Then commit this change and push it to `main`. Verify:
  - Build-Helm runs as a push run and succeeds, with no publishing job;
  - its "Extract chart info" log shows that the guard didn't fire and the version came from the fallback, 1.3.1, which proves `inputs.version` is empty on a branch push;
  - the `helm-chart` artifact, downloaded with `gh run download <id> -n helm-chart`, holds `example-net-helm-1.3.1.tgz`, and `helm show chart` on it shows `version` and `appVersion` 1.3.1;
  - Build-Dotnet and Check-Docker run for their own workflow file changes and pass;
  - no Release run starts;
  - GHCR's image and chart tags are unchanged.

  Result: commits `ef575aa` (the archive) and `6fcf9d6` (this change), pushed together.
  - Build-Helm run 42817635737, Build-Dotnet run 42817635653 and Check-Docker run 42817635680 all succeeded. Build-Helm had only the `Check-Helm` job.
  - Its log shows `INPUT_VERSION` empty, the guard not firing, and chart version 1.3.1 from the fallback.
  - The downloaded `helm-chart` artifact held `example-net-helm-1.3.1.tgz` with `version` and `appVersion` 1.3.1.
  - No Release run started.
  - GHCR's tags were unchanged (images `1.0` to `1.3.1` and `latest`, charts `1.0.0` to `1.3.1`), and `main` was at `6fcf9d6`.
- [x] 6.2 Ask before pushing, then push the tag `vtag-check-test` on the landed commit. This is the first time GitHub reads `release.yaml` and its call to Build-Helm. Verify:
  - the Release run fails at `Check-Tag` with the error naming the tag, and not at startup as an invalid workflow with no job logs. A startup failure would mean the file or the call is broken, and a real release would fail the same way;
  - `Test`, `Check-Helm`, `Build-and-Push-Docker`, `Sign-Docker-Image`, `Push-Helm` and `Sign-Helm-Chart` are skipped;
  - no Build-Helm run starts;
  - GHCR is unchanged and `main` has no new commit.

  Result: Release run 42817873564 on `6fcf9d6`.
  - It listed all 7 jobs, so GitHub accepted the workflow and its call to Build-Helm; it didn't fail at startup.
  - `Check-Tag` failed with `refs/tags/vtag-check-test is not a release tag. Use vMAJOR.MINOR.PATCH with no suffix`, and the other 6 jobs were skipped.
  - It was the only run started by the tag.
  - GHCR's tags were unchanged, and `main` stayed at `6fcf9d6`.
- [x] 6.3 Delete `vtag-check-test` locally and on GitHub. Verify with `git ls-remote --tags origin` that only `v1.0.0` to `v1.3.1` remain. Result: deleted in both places. Only `v1.0.0` to `v1.3.1` remain, locally and on GitHub, and deleting the tag started no run.
- [x] 6.4 Record here that the valid-tag path gets its first full run at the next `vX.Y.Z` release, and so do both re-run paths. List the checks from step 4 of the migration plan in `design.md`. Recorded: no valid tag has run through Release yet. Locally, valid tags pass `Check-Tag` and write their version, and the package path works at 1.3.2. On GitHub, the check-only path and the rejected-tag path have both run. Three things are still unproven:
  - the `version` handoff from `Check-Tag` to Build-Helm;
  - publishing and signing the chart from Release;
  - **Re-run failed jobs** and **Re-run all jobs** on a Release run.

  The next `vX.Y.Z` release (1.3.2 or higher) is the first full run. Check:
  - one Release run with the job graph in `design.md`, and `Check-Helm`'s checkout at the tag commit;
  - `helm show chart oci://ghcr.io/mchudinov/example-net-helm --version X.Y.Z` shows `version` and `appVersion` `X.Y.Z`;
  - the pulled `.tgz` has the same SHA-256 as the run's `helm-chart` artifact;
  - `cosign verify` passes for the image and the chart;
  - `main` has no new commit from the run;
  - Flux picks up `X.Y.Z`.
