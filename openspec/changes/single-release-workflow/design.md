## Context

See `proposal.md` (Why) for the problems measured on v1.3.1, and `specs/ci-test-gate/spec.md` for the required behavior.

The release flow today:

```
tag v1.3.1 --> Build-Docker run (tag commit)
                 Check-Tag --> Test --> Build-and-Push-Docker --+--> Sign-Docker-Image
                                                                +--> Update-Chart-Version (bot commit to main)
               Build-Helm run (workflow_run: main's newest commit)
                 Check-Helm --> Build-and-Push-Helm (packages again) --> Sign-Helm-Chart
```

Constraints that shape the approach:

- **Build-Dotnet already shows the pattern.** It is a reusable workflow (`workflow_call`) that keeps its own push and pull request triggers, and Build-Docker calls it as its `Test` job. A called workflow sees the caller's `github` context, and path filters don't apply to calls.
- **Build-Helm's checks are about 100 lines**, including a k3d cluster with a 5-minute limit, the label check script and a Polaris audit. Copying them into the release workflow, as EuGo-docint does for its smaller `helm lint` and `helm template` checks, would create two lists to keep in sync.
- **No workflow sets `concurrency`,** so a called workflow can't cancel its caller.
- **Checked locally with Helm v4.1.4 on the current chart:**
  - `helm package --version 1.3.2 --app-version 1.3.2` stamps both fields, and the default image becomes `example-net-docker:1.3.2`.
  - `helm lint <tgz> --strict` passes.
  - The label check passes on the `1.3.2+662a06b343e7` package.
- **Current action versions:** `actions/upload-artifact` v7.0.2 is the latest release and Build-Dotnet already uses `@v7`. `actions/download-artifact` is at v8.0.2. v8 fails on a digest mismatch by default. `azure/setup-helm@v5` no longer needs a token.
- **EuGo-docint moved to one release run per tag in `f29fb55`.** It stamps both versions from the tag and leaves `Chart.yaml` values as placeholders. But it checks the chart only after the image push, and lists checking first as deliberately deferred. This design adopts its versioning and closes that gap.

## Goals / Non-Goals

**Goals:**

- Every artifact of a release comes from the tag's commit, in one run.
- No image is pushed unless the tests and the chart checks have both passed.
- The `.tgz` that Release publishes is the file that was linted and installed on the test cluster.
- A release needs no write access to the repository.

**Non-Goals:**

- Making a release atomic. A failure after the image push (signing the image, pushing the chart or signing it) still leaves an image without a chart. See Risks.
- Changing what a push, pull request or manual run of Build-Helm checks. Those runs only gain the upload of their package.
- Signing the chart by digest. `Sign-Helm-Chart` moves as it is and still signs `name:version`.

## Decisions

### One run per tag, in a workflow renamed Release

```
Check-Tag --(version)--+--> Test        (calls build-dotnet.yaml) --+
                       |                                            +--> Build-and-Push-Docker --> Sign-Docker-Image --+
                       +--> Check-Helm  (calls build-helm.yaml) ----+                                                  |
                              packages the chart once                                                                  v
                              uploads it as artifact "helm-chart" .......................................> Push-Helm --> Sign-Helm-Chart
```

- `build-docker.yaml` is renamed to `release.yaml`, with `name: Release`, through `git mv` so its history follows.
- Nothing finds the workflow by name any more, and it now publishes the chart too.
- The rename matches EuGo-docint's `release` workflow.
- The cost is that runs before the rename stay listed under Build-Docker in the Actions tab.

**`Check-Helm` runs alongside `Test`, not after it.** Both have to pass before the image is pushed, so running them one after the other would only make the release longer.

Alternatives considered:

- **Keep two workflows and trigger Build-Helm on the tag as well.** Nothing would then order the chart after the image, and nothing would stop the image when the chart fails.
- **Do what EuGo-docint does:** one chart job after the image push that packages, renders and pushes the file. It is simpler and needs no artifact. But a chart that fails its checks then leaves a published image with no chart, which is one of the problems this change removes.

### Build-Helm becomes a reusable workflow that only checks

`build-helm.yaml` changes as follows:

- It gains `workflow_call`.
- It loses `workflow_run`, the `if` on `Check-Helm` that read it, `Build-and-Push-Helm` and `Sign-Helm-Chart`.
- Its push, pull request (same paths) and manual triggers stay.
- The workflow-level `GHCR_USERNAME`, `GHCR_PASSWORD` and `OIDC_ISSUER` variables go, because only the publishing jobs used them.

**Permissions.** The caller job in Release declares `permissions: contents: read`. The called `Check-Helm` job declares none, so it gets the caller's permissions. Push and pull request runs keep the repository default, as today.

### The version is passed in from Check-Tag

**Release side:**

- Check-Tag gets one more step after its regex check: `echo "version=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"`. Its `permissions: {}` stays.
- Release passes that value as `with: version: ${{ needs.Check-Tag.outputs.version }}`.

**Build-Helm side:**

- `workflow_call` declares a `version` input, `required: false`, default `''`.
- `Check-Helm` reads it through the step's `env` (`INPUT_VERSION: ${{ inputs.version }}`).
- Push, pull request and manual runs have no inputs, so they fall back to `Chart.yaml`'s version.
- Both fields are read with anchored matches: `sed -n 's/^version: //p'` and `sed -n 's/^name: //p'`. The current unanchored `grep 'version:' | tail -n1` would read a comment line containing `version:`.

The release version is then decided in one place, by the same job that checks the tag.

**The fallback must never apply inside a release.** GitHub doesn't flag a misspelled job output or a missing `with:`. It passes an empty string. The fallback would then package the chart at `Chart.yaml`'s 1.3.1, while the image goes out at the tag's version, and `Push-Helm` would replace the published chart 1.3.1. Two guards stop this:

- **"Extract chart info" fails when its input is empty in a tag push run.** The condition is: `GITHUB_EVENT_NAME` is `push`, `GITHUB_REF` starts with `refs/tags/`, and `INPUT_VERSION` is empty. Then the step prints an `::error::` and exits 1.
  - Build-Helm's own push trigger is limited to `main`, so a tag push reaches this step only through Release.
  - A manual run reports `workflow_dispatch`, even on a tag ref, so it still uses the fallback.
  - The failure comes before the image push, so a wiring mistake publishes nothing.
- **`Push-Helm` checks that `chart_version` equals `${GITHUB_REF_NAME#v}` before `helm push`,** and fails otherwise. This runs after the image push, but it keeps a mismatched chart from replacing a published version even if the first guard is later broken.

Alternatives considered:

- **Read `GITHUB_REF_NAME` inside the called workflow.** It works, because the called workflow sees the tag. But a manual Build-Helm run started on a tag ref would then also build a chart at that tag's version.
- **Recompute the version in each job, as EuGo-docint does.** That fits EuGo's chart job, which only ever runs on a tag. Build-Helm here also runs outside releases and needs a fallback.

### Package once, check that file, publish that file

On every run, `Check-Helm` packages the chart once into `$RUNNER_TEMP`:

```
helm package ./ --version "$VERSION" --app-version "$VERSION" --destination "$RUNNER_TEMP"
```

Push and pull request runs use the same command a release does, so every chart change exercises it. The steps then use that file:

- **Lint:** `helm lint "$CHART_PACKAGE" --strict`, instead of the directory.
- **Dry-run install:** on the package, instead of `./`.
- **Test install on k3d:** on the package, as today.
- **Build-metadata package:** stays separate and is the label check's deliberate input. It now also gets `--app-version "$VERSION"`, to match what Flux makes of a published chart.
- **Polaris:** keeps auditing the `helm/` directory. Its handling of an archive hasn't been verified, and the templates are identical; only the two version fields differ.

Nothing is packaged into `helm/` any more. The ordering comment about `.helmignore` not excluding `*.tgz` therefore goes, and the build-metadata package no longer has to come first.

### Hand the package over as a run artifact

**Upload.** The last step of `Check-Helm`, after Polaris, uploads the package:

- `actions/upload-artifact@v7`, artifact name `helm-chart`;
- `if-no-files-found: error`;
- `overwrite: true`;
- default retention.

Placed last, the upload means a failed check uploads nothing. `overwrite: true` lets **Re-run all jobs** upload again under the same name. Retention stays at the repository default (90 days for a public repository), which covers GitHub's 30-day re-run window. The upload runs on every run, not only releases, so the push that lands this change proves it works. It costs a few kilobytes per run.

**Workflow outputs.** `workflow_call` exposes these from `Check-Helm`'s job outputs:

- `chart_name`
- `chart_version`
- `chart_file`, the package's file name
- `registry_lowercase`

**Download.** `Push-Helm` downloads the artifact with `actions/download-artifact@v8` into `${{ runner.temp }}/helm-chart`. v8 fails the step on a digest mismatch, so a corrupted transfer can't be published.

Alternative considered:

- **Package again in `Push-Helm` from the tag's commit.** It's the same source, but the published file would no longer be the installed file, which is what the spec requires.

### Push-Helm and Sign-Helm-Chart move into Release

**`Push-Helm`:**

- `needs: [Check-Helm, Sign-Docker-Image]`, so the chart is still pushed only after the image has been signed.
- `permissions: packages: write` and no checkout.
- Steps:
  1. `azure/setup-helm@v5`
  2. download the artifact
  3. `helm registry login ghcr.io`, with the user and token passed through the step's `env` and read as `"$GHCR_PASSWORD"`, not `${{ }}` inside the script
  4. the version check described under "The version is passed in from Check-Tag"
  5. `helm push "$RUNNER_TEMP/helm-chart/$CHART_FILE" "oci://$REGISTRY"`, with both values passed through `env`

  A separate helm login is required because Helm keeps its own registry credentials. `Sign-Helm-Chart` keeps `docker/login-action`, because that is the login cosign uses.

**`Sign-Helm-Chart`** moves unchanged except for its `needs` (`[Check-Helm, Push-Helm]`) and where its outputs come from. It still signs `registry/name:version` and verifies the signature.

**Release's workflow-level `env`:** `HELM_WORKING_DIRECTORY` and `BRANCH` go, because only `Update-Chart-Version` used them. `REGISTRY`, `GHCR_*` and `OIDC_ISSUER` stay for the signing jobs.

**Removals:**

- `Update-Chart-Version`, together with its `contents: write` and the `mikefarah/yq` and `planetscale/ghcommit-action` references.
- `Build-and-Push-Docker` doesn't change, apart from `needs: [Test, Check-Helm]`.

### `Chart.yaml` values become placeholders

`version: 1.3.1` and `appVersion: "1.3.1"` stay. Their two comments are replaced by one block, adapted from EuGo-docint. It says:

- a release tag `vX.Y.Z` publishes the image and the chart at `X.Y.Z`;
- CI stamps both values at package time, so these are placeholders;
- they aren't edited for a release;
- a local `helm install ./helm` deploys image 1.3.1 unless `image.tag` is set.

The block must not contain the text `version:`. The anchored reads would cope, but it keeps the file unambiguous.

Alternative considered:

- **`0.0.0`.** That makes the placeholder obvious, but a local install would then pull an image tag that doesn't exist, and every rendered label would change for no change in behavior.

### Documentation

**`CLAUDE.md`:**

- **CI/CD item 3:** describes Release: the job graph, one run per tag, and no write to the repository.
- **CI/CD item 4:** describes Build-Helm as the checks that Release calls and that run on pushes, pull requests and manual runs. It publishes nothing.
- **"If a release's chart publishing fails":** use **Re-run failed jobs** on the Release run. That keeps the tag's commit, but hasn't been used on a release yet.
- **New rule:** never push a release tag on a commit older than this change.
- **Removed:** the "Never name it `Build-Docker`" paragraph and the sentence about Build-Helm finding Build-Docker by name.
- **Renamed:** `Build-Docker` becomes Release wherever it means the workflow (items 2 and 3 and "Default image").
- **The closing "Do not hand-edit" sentence** says the version fields are placeholders that a release stamps from its tag.

**Workflow comments:**

- `check-docker.yaml` drops its "Never name this workflow Build-Docker" comment and points its two Build-Docker references at Release and `release.yaml`.
- `build-dotnet.yaml`'s comment says Release calls it.

## Risks / Trade-offs

- **[k3d and Polaris now stand between a tag and its image]** A stuck test cluster or a flaky install delays the image as well as the chart. This is intended, so that a chart failure can't leave a lone image. → **Re-run failed jobs** on the same run. The 5-minute limit on the k3d step keeps a stuck cluster from hanging the release.
- **[The k3d install runs before the release's image exists]** The pods can't pull image `X.Y.Z` yet. The install passes only because `helm install` doesn't use `--wait`. → A comment on the install step says not to add `--wait` or `--atomic`.
- **[A failure after the image push still leaves an image without a chart]** This applies to `Sign-Docker-Image`, `Push-Helm` and `Sign-Helm-Chart`. → For a one-off failure, **Re-run failed jobs**: `Push-Helm` downloads the same artifact. If the fix needs a commit, push the next patch tag. The failed version then stays an image without a chart, as in EuGo-docint.
- **[The version input arrives empty in a release]** The cause would be a typo in Check-Tag's output, or a missing `with: version:`. The chart would fall back to 1.3.1 and replace the published 1.3.1. Neither the local checks nor the test tag run this wiring, because the test tag stops at Check-Tag. → The two guards under "The version is passed in from Check-Tag". The first fails before the image push. The second fails before `helm push`.
- **[The re-run paths are untested]** Two things haven't been tried here: whether attempt 2 can download attempt 1's artifact, and whether `overwrite: true` is enough for **Re-run all jobs**. Both are documented behavior. → `CLAUDE.md` says the paths haven't been used on a release yet.
- **[Switch-over: a tag on a commit from before this change]** That commit's old Build-Docker runs. It pushes the image and bot-commits over the placeholder. `main`'s `build-helm.yaml` no longer listens for `workflow_run`, so no chart is published. → The `CLAUDE.md` rule. Every existing tag (v1.0.0 to v1.3.1) is already fully released, and new tags come from `main`.
- **[The valid-tag path is first proven by a real release]** → The push that lands this change proves the check-only path, the fallback version and the upload. The test tag proves the job graph's failure path. Don't test with a low throwaway release such as `v0.0.1`: `latest=auto` would move `latest` back to it, and Flux's `1.x` wouldn't pick it up.
- **[`main`'s `Chart.yaml` stops matching releases]** → Nothing reads it. Flux installs from the OCI repository, and the README's install examples use the registry or a local package. The new comment says what a local install deploys.
- **[Run history splits]** Runs before the rename stay under Build-Docker. → Accepted.

## Migration Plan

1. Optionally, commit the archived `enforce-release-tag-format` change first, so the two changes stay apart in history.
2. Land this change in one commit on `main`. The push changes `build-helm.yaml`, so Build-Helm runs as a push run. Check that run:
   - it checks the chart at the fallback version 1.3.1;
   - it uploads `helm-chart`;
   - it publishes nothing.

   Release starts no run, because it has only a tag trigger.
3. Push the test tag `vtag-check-test` on the landed commit, then check the Release run:
   - Check-Tag fails;
   - `Test`, `Check-Helm` and every later job are skipped;
   - no other run starts.

   Delete the tag locally and on GitHub.
4. The first real release, `v1.3.2` or higher, is the full proof:
   - one Release run with the graph above, and `Check-Helm`'s checkout at the tag commit;
   - `helm show chart oci://ghcr.io/mchudinov/example-net-helm --version X.Y.Z` shows `version` and `appVersion` `X.Y.Z`;
   - the pulled `.tgz` has the same SHA-256 as the run's `helm-chart` artifact;
   - `cosign verify` passes for the image and the chart;
   - `main` has no new commit;
   - Flux picks up `X.Y.Z`.

**Rollback:** revert the commit. The two-run flow and the bot commit return, and anything already published stays.

## Open Questions

- Does GitHub hand attempt 1's artifact to a `Push-Helm` re-run in attempt 2? It is expected to, and it doesn't change the specs or the tasks. The first re-run of a release will show it.
