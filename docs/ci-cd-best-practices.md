# CI/CD best practices

This page lists the practices this repository's pipeline follows, or should follow, for building, testing, signing and releasing a .NET service as a container image and a Helm chart with GitHub Actions. For each practice it explains why it matters, what the workflows in [`.github/workflows/`](../.github/workflows/) do today, and what is still missing.

Status:

- ✅ in place
- ⚠️ partly in place
- 📌 left out on purpose
- ❌ missing
- ❓ written but never run for real

Line references were checked against the workflows when this page was written. If you change a workflow, update its line here as well.

- [Triggers and gating](#triggers-and-gating)
- [Security and supply chain](#security-and-supply-chain)
- [.NET build and test](#net-build-and-test)
- [Container image](#container-image)
- [Helm chart](#helm-chart)
- [Release](#release)
- [What to do next](#what-to-do-next)

## Triggers and gating

### Every input to a build triggers the check that covers it ⚠️

A path filter decides which changes run which check. A filter that lists what to include fails open: a new input that nobody adds to the list is never checked. A filter that lists what to skip, such as docs only, fails safe, but it costs extra runs.

All four workflows use include lists. Three inputs aren't covered:

- `.github/polaris-config.yaml` isn't in Build-Helm's `paths` ([`build-helm.yaml:28-39`](../.github/workflows/build-helm.yaml)). A change to the policy that gates the chart runs nothing.
- `release.yaml` runs only on a tag. A change to it isn't checked by anything until the next release. Workflow linting (below) is the cheapest cover for this.
- A future root-level build file, such as `Directory.Build.props`, would trigger neither Build-Dotnet nor Check-Docker. When you add one, add it to both `paths` lists. CLAUDE.md already says this for Check-Docker.

### A red check blocks the change 📌

GitHub can refuse a change whose checks failed. On a public repository a ruleset that requires status checks on `main` is free.

`main` has no branch protection and no ruleset. That is a trade-off, not an oversight, because this repository is committed straight to `main`:

- **Changes would have to come through pull requests.** A commit pushed directly can't have passed its checks yet. The alternative is for the owner to bypass the rule.
- **Path-filtered checks can't simply be required.** A required check from a workflow that the paths skipped stays "Expected" and leaves the pull request pending forever. You need a workflow that always runs and ends with one summary job, and you make only that job required.

### Superseded runs are cancelled ❌

When a second push arrives, the run for the first one is usually wasted. A `concurrency` group cancels it.

None of the workflows set `concurrency`. Two things to get right:

- **Never cancel a release in progress.** Cancelling could leave an image pushed without its chart. In `release.yaml`, set `group: release-${{ github.ref }}` and `cancel-in-progress: false`.
- **Give each reusable workflow its own group name.** In a called workflow, `github.workflow` is the caller's name. If Build-Dotnet and Build-Helm both used `${{ github.workflow }}-${{ github.ref }}`, their groups would be identical when Release calls them, and one would cancel the other. Use a literal prefix, for example `group: build-helm-${{ github.ref }}`.

### Every job has a time limit ⚠️

A job with no `timeout-minutes` runs for up to six hours. Before Build-Helm got its step timeout, the k3d step once hung for nearly three hours until someone cancelled it by hand.

Today only that one step has a limit: `timeout-minutes: 5` ([`build-helm.yaml:122`](../.github/workflows/build-helm.yaml)). Set `timeout-minutes` on every job too. A job that calls a reusable workflow (`uses:`) can't take `timeout-minutes`, so set the limit on the jobs inside Build-Dotnet and Build-Helm.

## Security and supply chain

### The workflow token has the least privilege it needs ⚠️

Set `permissions:` at the top of every workflow, as `contents: read` or `{}`, and widen it only on the jobs that need more. Without that block, the token gets the repository default, and a change to the repository settings would quietly widen it.

What the workflows do now:

- **Build-Dotnet and Check-Docker:** set `contents: read` at the top. ✅
- **Release:** sets permissions on every job. `Check-Tag` gets `{}`. ✅
- **Build-Helm:** has no `permissions:` block at all. ⚠️ On its own runs it gets the repository default, which is read today. When Release calls it, Release's `contents: read` applies.

### Secrets reach only the steps that use them ❌

A secret in workflow-level `env:` can be read by every step of every job in that workflow, including third-party actions.

`release.yaml:13-14` puts `GITHUB_TOKEN` into the workflow-level `env` as `GHCR_PASSWORD`. Workflow `env` doesn't pass into reusable-workflow calls, and `Check-Tag`'s token has no permissions. The exposure is the four jobs that hold `packages: write`: `Build-and-Push-Docker`, `Sign-Docker-Image`, `Push-Helm` and `Sign-Helm-Chart`. Every `run:` step in them, and every process those steps start, has a token that can push packages in its environment.

Fix: pass `password: ${{ secrets.GITHUB_TOKEN }}` straight to `docker/login-action`, and give the `helm registry login` step its own step-level `env:`.

This narrows the exposure but doesn't remove it. Many actions, such as `docker/metadata-action` and `azure/setup-helm`, take the token through an input that defaults to `${{ github.token }}`, so they receive it anyway. The real boundary is each job's `permissions:` block. Keep `packages: write` on the jobs that push or sign, and nowhere else.

### Expressions reach scripts through `env:`, never inside `run:` ⚠️

GitHub substitutes `${{ }}` into a `run:` script before the shell sees it. A value an attacker can set, such as a branch name or a PR title, then runs as code. The safe form maps the value to a variable in the step's `env:` and quotes `"$VAR"` in the script. Some steps already do this, for example `Package helm` and `Dry-run install` in `build-helm.yaml`, and `Check that the chart version is the tag's` in `release.yaml`.

Eleven steps still put `${{ }}` inside `run:`:

- `build-dotnet.yaml:47`, `:54-55`
- `build-helm.yaml:81`, `:86-88`, `:188-192`
- `release.yaml:84-86`, `:90-93`, `:138`, `:144-146`, `:213`, `:219-221`

None of these values can be set by an attacker today. They are the owner name, the registry, digests, and versions from a tag that `Check-Tag` has already validated. The risk is a later edit that adds one that can.

### Actions are pinned ⚠️

CLAUDE.md requires a release tag or a commit SHA, never a branch. Every action follows that today, with a tag.

A tag can be moved to different code; a full commit SHA can't. The stronger form is a full SHA with the tag in a comment (`uses: actions/checkout@<sha> # v7`). Dependabot keeps such pins current.

### Tool versions are pinned and downloads are verified ⚠️

A floating tool version means the same commit can build differently next week.

What's pinned:

- **k3d:** version plus SHA-256 ([`build-helm.yaml:110-117`](../.github/workflows/build-helm.yaml)). ✅
- **Polaris:** `POLARIS_VERSION`. ✅
- **cosign:** comes from the pinned `cosign-installer` tag. ✅

What floats:

- **Helm** floats: `azure/setup-helm` has no `version:` ([`build-helm.yaml:61`](../.github/workflows/build-helm.yaml), [`release.yaml:159`](../.github/workflows/release.yaml)). ⚠️
- **Base images** are referenced by tag, not digest (`sdk:10.0` and `aspnet:10.0-noble-chiseled` in the [`Dockerfile`](../Dockerfile)). ⚠️ A digest pin makes rebuilds reproducible. Dependabot can bump it when Microsoft ships a patched image.
- **The .NET SDK** floats within 10.0 (`10.0.x` plus `latestFeature` in `global.json`). That is deliberate: it picks up servicing fixes. 📌

### Dependencies are updated automatically ❌

The repository has no `.github/dependabot.yml`. Configure it for three ecosystems:

- `github-actions`
- `nuget` (directory `/src/ExampleNet` and `/tests/ExampleNetTests`)
- `docker` (directory `/`)

Weekly grouped updates keep the noise low.

### Known vulnerabilities fail the build ❌

Nothing fails on a vulnerable package or base image. NuGet audit only warns. Two ways to close this:

- **Packages:** fail Build-Dotnet on `dotnet list src/ExampleNet.slnx package --vulnerable --include-transitive` output, or treat the `NU1901`–`NU1904` audit warnings as errors. The second option is a project-file change, so it goes to the `dotnet-developer` agent.
- **Image:** scan the built image (for example with Trivy) in Check-Docker and in Release before the push.

### Releases are signed by digest and verified against the exact signer ⚠️

A signature by tag signs whatever the tag points at when cosign resolves it. A verify step that accepts a loose identity proves little.

What the release does today:

- **The image is signed by digest** ([`release.yaml:138`](../.github/workflows/release.yaml)). ✅
- **The chart is signed and verified by tag** (`release.yaml:213`, `:219`). ⚠️ `helm push` prints the digest it pushed. Capture it and sign `.../example-net-helm@sha256:...`.
- **The verify identity is too loose** (`release.yaml:145`, `:220`). ⚠️ The regex `.+github.com\/<owner>.+` doesn't stop at the owner name, so `github.com/mchudinov-anything/...` passes. It also accepts any repository and any workflow of the owner. Verify the exact identity instead: `--certificate-identity "https://github.com/$GITHUB_REPOSITORY/.github/workflows/release.yaml@$GITHUB_REF"`.
- **Rekor is off.** ❓ The signing config still includes Sigstore's timestamp authority (`timestamp.sigstore.dev`), so each signature should carry a trusted timestamp that keeps it verifiable after the 10-minute Fulcio certificate expires. Nobody has verified a released artifact after expiry yet. Do that once, and publish the consumer's `cosign verify` command in the README.

### Workflows are linted ❌

Two tools cover different problems:

- **actionlint** checks workflow syntax and expression types, and runs shellcheck on `run:` scripts.
- **zizmor** finds security problems: template injection, excessive permissions, unpinned actions, and secrets in broad scopes.

Run both on any change under `.github/`. This is the only check `release.yaml` would get before a tag.

## .NET build and test

### Restore once, then build and test without repeating it ✅

`dotnet restore`, then `build --no-restore`, then `test --no-build`, all against `src/ExampleNet.slnx` ([`build-dotnet.yaml:43-58`](../.github/workflows/build-dotnet.yaml)).

### CI builds the configuration that ships ✅

Build-Dotnet builds and tests `Release`, and the Dockerfile publishes `Release`.

### Test results and coverage are published ✅

What Build-Dotnet does with test output:

- writes TRX results and cobertura coverage to `TestResults/`, and uploads them even when a step fails;
- writes a summary of the test run to the run page;
- fails the run when no tests are discovered.

There is no coverage threshold. That is optional for a service this small.

### NuGet packages are cached ❌

`setup-dotnet` can cache the package folder (`cache: true`). It needs lock files to key the cache:

- `RestorePackagesWithLockFile` in the project files, which is a `dotnet-developer` change;
- `--locked-mode` on restore, so CI fails when a lock file is out of date.

The win is small while restore is fast. It grows with the dependency list.

## Container image

### Multi-platform images are cross-compiled ✅

The SDK stage runs on the build machine's platform and publishes for `$TARGETARCH`. The runtime stage has no `RUN`, so the arm64 image needs no emulation (see the [`Dockerfile`](../Dockerfile) and CLAUDE.md).

### CI builds the image exactly as the release does ⚠️

Check-Docker and Release use the same action, the same default builder and the same platform list. The lists are kept equal only by a comment on each ([`check-docker.yaml:42-43`](../.github/workflows/check-docker.yaml), [`release.yaml:107-108`](../.github/workflows/release.yaml)), and Release doesn't reuse Check-Docker the way it reuses Build-Dotnet and Build-Helm.

Two ways to make the platform lists stay equal:

- **One shared workflow:** turn the image build into a reusable workflow with a `push` input, and call it from both.
- **A check step:** add a step that fails when the two `platforms:` lines differ.

### Docker layers are cached 📌

Check-Docker deliberately builds with no layer cache, and the image builds quickly without one. If build time becomes a problem, add `cache-from: type=gha` and `cache-to: type=gha,mode=max`, and share the cache with Release.

### The runtime image is small and doesn't run as root ✅

The runtime base is `aspnet:10.0-noble-chiseled`: no shell, no package manager, and a non-root default user.

- **In the cluster:** the chart enforces non-root with `runAsNonRoot: true` ([`helm/values.yaml:36`](../helm/values.yaml)).
- **With `docker run`:** the base image's default user is the only guarantee. A `USER $APP_UID` line in the Dockerfile would make that explicit.

### The build context holds only what the build needs ✅

The Dockerfile copies `global.json` and `src/ExampleNet/` and nothing else. `.dockerignore` keeps `bin/`, `obj/`, tests, the chart and local settings out of the context.

## Helm chart

### The chart is linted strictly and every template is rendered ⚠️

Build-Helm checks the chart in two ways:

- **Lint:** runs `helm lint --strict` on the packaged chart with its default values.
- **Label check:** [`check-chart-labels.sh`](../.github/scripts/check-chart-labels.sh) renders two value sets that together reach every template that sets labels, and fails on an invalid label value.

The Argo Rollout templates are rendered only by the label check. Nothing validates them against a schema, because k3d has no Rollout CRDs. Running `kubeconform` with the Argo CRD schemas on each rendered value set would close that.

### The chart is installed into a real cluster and its pods start ⚠️

Build-Helm installs the packaged chart into k3d twice: with the plain version, and with a Flux-style `+build` version. Neither install uses `--wait` (`build-helm.yaml:167-177`), because in a release the image isn't pushed yet.

This proves the API server accepts the manifests. It doesn't prove that the pod starts and passes its probes under the chart's security context: a read-only root filesystem, non-root, and all capabilities dropped. To prove that:

1. build the image in the same job;
2. load it with `k3d image import`;
3. install with `--set image.repository=... --set image.tag=...`;
4. add `--wait --timeout 3m`.

Build-Helm then also depends on `src/**` and the `Dockerfile`, so its path filters would need to grow.

### A policy audit gates the chart ✅

Fairwinds Polaris audits the chart with [`.github/polaris-config.yaml`](../.github/polaris-config.yaml). It fails on any "danger" finding or a score below 90. Changes to the chart's security context, probes or resources must keep it passing. That config file is missing from Build-Helm's `paths` (see [Triggers and gating](#triggers-and-gating)).

### The published chart is the exact file that was checked ✅

Build-Helm packages the chart once and uploads it as the `helm-chart` artifact. Release downloads that file, checks that its version equals the tag's ([`release.yaml:173-180`](../.github/workflows/release.yaml)), and pushes it unchanged.

## Release

### The tag is validated before anything runs ✅

`Check-Tag` accepts only `vMAJOR.MINOR.PATCH` and fails anything else within seconds, release candidates included ([`release.yaml:29`](../.github/workflows/release.yaml)). Every other job waits for it.

### The tag versions everything, and a release writes nothing to the repository ❓

The image and the chart both take their version from the tag. `Chart.yaml`'s version is only a placeholder.

The single Release workflow has so far run only on a test tag, which it rejected as intended. Every published release so far was built by the workflow it replaced. Prove it before the next real release: either push a tag in a fork, where the workflows derive the registry from the fork's owner, or push the next patch tag. Never tag a commit older than the one that added `release.yaml` (see CLAUDE.md).

### Nothing is published until every check passes ✅

The image push waits for both `Test` and `Check-Helm` (`needs: [Test, Check-Helm]`). The chart is pushed only after the image is signed.

### The release reuses the CI workflows ⚠️

`Test` calls Build-Dotnet and `Check-Helm` calls Build-Helm through `workflow_call`. So the checks a release runs are exactly the ones that run on a push. The image build is the exception, as described under [Container image](#container-image).

### Inline scripts run in strict mode ⚠️

When a step doesn't name a shell, GitHub runs it with `bash -e`, without `pipefail`. A failure on the left of a pipe is then ignored. [`check-chart-labels.sh`](../.github/scripts/check-chart-labels.sh) uses `set -euo pipefail`, but the inline steps don't.

Add the following to every workflow. It gives each inline step `bash --noprofile --norc -eo pipefail`:

```yaml
defaults:
  run:
    shell: bash
```

No inline step hides a failure today, so this is a safeguard for future edits.

### Errors are annotated and logs are grouped ✅

Failures print `::error::` annotations with the reason, and a failed k3d start dumps each node container's logs in its own group.

## What to do next

In order of value for effort:

1. **Scope the token in `release.yaml`.** Move `GHCR_PASSWORD` out of the workflow-level `env` into the login steps. Then move every `${{ }}` inside `run:` into step `env:`.
2. **Tighten signing.** Verify the exact certificate identity, sign the chart by digest, and verify one released artifact after its certificate has expired.
3. **Lint the workflows.** Add actionlint and zizmor for changes under `.github/`.
4. **Close the small gating gaps.** Add `permissions: contents: read` to `build-helm.yaml`, `.github/polaris-config.yaml` to its `paths`, `concurrency` groups, and `timeout-minutes` on every job.
5. **Automate updates.** Add Dependabot for actions, NuGet and Docker, then switch the action pins to commit SHAs.
6. **Prove the Release workflow** on a tag in a fork before the next real release.
7. **Prove the pods start.** Install into k3d with a locally built image and `--wait`.
8. **Add a vulnerability gate** for packages and the image.

Requiring checks before merging is left out of this list. It would change how this repository is worked on: today changes are committed straight to `main`, without pull requests.
