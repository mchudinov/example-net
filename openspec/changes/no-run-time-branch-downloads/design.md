## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-10:

- **k3d-action.**
  - `AbsaOSS/k3d-action@v2` last had a release in v2.4.0, on 2023-01-12.
  - Its `run.sh` always runs `curl --silent --fail https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | TAG=<version> bash` (lines 27 and 68). It has no input that skips this.
  - It then runs `k3d cluster create <name> --wait <args>`, and loops until `kubectl get nodes` shows no node `NotReady`.
- **k3d's release.** The v5.9.0 release has `k3d-linux-amd64` and a `checksums.txt` whose line for it reads `06d8f25bc3a971c4eb29e0ff08429b180402db0f4dec838c9eac427e296800a0  _dist/k3d-linux-amd64`. The release isn't immutable.
- **Order in `Check-Helm`.** Since 6fcf9d6 the job runs:
  1. it packages the release chart;
  2. it lints that package;
  3. it creates the k3d cluster, which is where `install.sh` runs;
  4. the dry-run, the label checks, both installs and Polaris;
  5. it uploads the package, which Release then publishes.

  Before 6fcf9d6, the published package was made by a fresh job, `Build-and-Push-Helm`.
- **The stuck-cluster requirement.** The requirement "Chart checks stop waiting for a test cluster that doesn't come up" relies on three things:
  - the cluster step's `timeout-minutes: 5`;
  - its `id: k3d`, which the diagnostics step tests with `steps.k3d.outcome == 'failure'`;
  - the containers' `app=k3d` label, which the diagnostics step filters on.
- **The signing config's history.** Only one commit, `aa440a9b8612b7d016d6052468e7d5703948f26c` (2025-07-07), has ever changed `targets/signing_config.v0.2.json` in `sigstore/root-signing`. The file has the same sha256, `9711a6d5375706957a4859af31c5866a4474f81f0544f9f4b76c9c4f4c8a539c`, at that commit, on `main`, and in Sigstore's TUF repository (targets v14).
- **cosign v3.1.4.** This is what `sigstore/cosign-installer@v4.1.3` installs by default. It documents `signing-config create --with-default-services` ("use the Sigstore TUF root as default values") and `--no-default-rekor`.
- **The spike (2026-11-10).** cosign v3.1.4's Windows binary, checked against the release checksum, generated a config locally. Compared with today's `curl | jq`:
  - **Same:** media type, `caUrls`, `oidcUrls`, `tsaUrls` and `tsaConfig`. Neither file has `rekorTlogUrls`.
  - **Same values, different format:** timestamps are written `.000Z` in today's file and `Z` in the generated one.
  - **Different:** `rekorTlogConfig` is `{}` in the generated file and `{"selector":"ANY"}` in today's.
- **Where cosign reads the Rekor selector.** In cosign v3.1.4:
  - `ShouldUploadToTlog` returns false when the config has no Rekor URLs (`signcommon/common.go:126`). Both `sign` (`sign.go:200-207`) and `sign-blob` (`sign_blob.go:65-74`) then drop Rekor.
  - The selector is read only in sigstore-go v1.3.0's `SelectServices`, which this path doesn't reach.
  - cosign itself leaves `rekorConfig` empty when no Rekor URL is set (`common.go:702-714`).
- **kind v0.33.0.**
  - A `--wait` timeout logs a warning and returns success (`waitforready.go:88-91`).
  - A failed create deletes the node containers unless `--retain` is set (`create.go:103-106`).
  - `helm/kind-action@v1.15.2` has no input for `--retain`.
- **Other actions' downloads.** Every third-party `uses:` reference was scanned at its referenced version. The scan read every `.js`, `.cjs`, `.mjs`, `.sh`, `.ps1` and `.yml`/`.yaml` file under the action's path, plus `Dockerfile`s. It skipped `node_modules`, test, docs and licence folders. That includes `setup-dotnet`'s `dist/setup`, `dist/cache-save` and `externals`, and `upload-artifact`'s `dist/upload` and `dist/merge`. It searched for:
  - `raw.githubusercontent.com` URLs;
  - GitHub `archive/` and `raw/` URLs;
  - `refs/heads/`;
  - `main` or `master` passed as a `ref`.
  - **`AbsaOSS/k3d-action`** is the only one that downloads from a branch.
  - **`actions/checkout`** contains `refs/heads/…` only as ref strings. It fetches this repo.
  - **`sigstore/cosign-installer@v4.1.3`** fetches `release-cosign.pub` at the cosign release tag.
  - **`docker/build-push-action@v7`, `docker/login-action@v4` and `docker/metadata-action@v6`** bundle `@docker/actions-toolkit` (v0.100.0 in `build-push-action`). Its installers download `.github/*-releases.json` from `docker/actions-toolkit`'s `main`. Neither `build-push-action`'s nor `metadata-action`'s source calls an installer, and `Toolkit`'s constructor only creates the objects. So that code doesn't run here. It could start running in a later release under the same major tag, which this change leaves as it is.
- **Triggers.** No workflow runs on a change to `release.yaml`. Build-Helm, Build-Dotnet and Check-Docker each filter on their own paths, and Release runs only on `v*` tags.

## Goals / Non-Goals

**Goals:**
- Nothing a workflow runs or reads while it runs comes from a branch of another repository.
- Nothing that's published or signed changes. The k3d version, the cluster and the signing services all stay the same.
- The signing config still follows Sigstore's own updates, which was D5's reason in `pin-floating-action-refs`. It now gets them through Sigstore's signed TUF repository, not a branch.

**Non-Goals:**
- **Protecting the chart package from every later step in `Check-Helm`.** That needs a seal on the package or a separate job, and is its own change.
- **Fixed references for every action and binary.** Major-version tags, Helm's "latest" install and the Polaris binary download stay as they are.

## Decisions

Each decision is marked *agreed* (decided with the user) or *planning default* (proposed, open to change at review).

### D1. Install k3d from its release binary, checked against a hash in the workflow (agreed)

The first step downloads `k3d-linux-amd64` for the named version into `$RUNNER_TEMP`, checks its sha256, and installs it with `sudo install` into `/usr/local/bin`. The version and the hash are environment variables of that one step, so a bump changes both in one place.

The hash is written in the workflow, not read from the release's `checksums.txt`. The release isn't immutable, so a checksum from the same release would catch a corrupted download but not a replaced one.

The cluster tool stays k3d. So the `app=k3d` label, and the behaviour that a timeout kills only the k3d client and leaves the containers for the log step, both stay as the stuck-cluster requirement needs them.

**Alternatives considered:**
- **kind.** See D7.
- **Pinning `AbsaOSS/k3d-action` to a commit SHA.** Its `run.sh` downloads `install.sh` from `main` whichever commit runs.
- **Checking against the release's `checksums.txt`.** See above.

### D2. The cluster step keeps the action's wait for Ready nodes (agreed)

The step keeps `id: k3d` and `timeout-minutes: 5`. It runs `k3d cluster create test-cluster --agents 1 --wait` and then `kubectl wait --for=condition=Ready nodes --all --timeout=5m`. That is the action's own command plus its node loop, so the stuck-cluster scenarios keep holding:
- A hang anywhere in the step ends at the step's limit.
- The diagnostics step still tests this step's outcome.

`kubectl wait` gets the same 5 minutes. The step's `timeout-minutes` therefore stays the only limit that ends it, so the workflow names one limit. `kubectl` is installed on `ubuntu-latest`, and the action relied on it too.

The download step has no `id`. A failed download isn't a cluster failure, and no cluster containers exist yet, so the diagnostics step doesn't run, as "Other failures don't show cluster logs" requires.

### D3. cosign builds the signing config from Sigstore's TUF repository (agreed; supersedes D5 of `pin-floating-action-refs`)

In both signing jobs, `Create signing config without Rekor` runs `cosign signing-config create --with-default-services --no-default-rekor --out "$RUNNER_TEMP/signing-config-no-tlog.json"`, after `Install cosign`. The output path is unchanged, so the sign and verify steps don't change.

It keeps D5's reason, following Sigstore's updates, and cosign verifies the content against the TUF root it ships with before using it. The `curl` and the `jq` leave both jobs. The spike produced the same services as today's file. Its one real difference, `rekorTlogConfig: {}`, is never read on this path (see Context).

**Alternatives considered:**
- **The pinned URL.** It's the fallback, D4.
- **Keeping D5.** The new requirement wouldn't hold.
- **Committing the JSON to the repo.** It does the same as the pin, with one more file to keep.

### D4. Fallback: the download pinned to a commit (agreed)

The fallback is used only if the branch proof (D5) fails. Both steps then keep `curl | jq`, but:
- they read `https://raw.githubusercontent.com/sigstore/root-signing/aa440a9b8612b7d016d6052468e7d5703948f26c/targets/signing_config.v0.2.json` with `curl -fsSL`;
- they check its sha256 against `9711a6d5375706957a4859af31c5866a4474f81f0544f9f4b76c9c4f4c8a539c` before `jq`.

That file is byte-identical to today's. The fallback gives up following Sigstore's updates. That's acceptable, because the file has changed once ever. The specs hold either way, and choosing the fallback changes only task group 3.

### D5. The signing config is proven on a throwaway branch before it reaches `main` (agreed)

The proof is a temporary workflow, `.github/workflows/tmp-signing-config-proof.yaml`, on the branch `tmp/signing-config-proof`:
- **Trigger and permissions:** `on: push: branches: [tmp/signing-config-proof]`, and `permissions: id-token: write` only. It has no checkout.
- **Steps:**
  1. install cosign with `sigstore/cosign-installer@v4.1.3`;
  2. create the config with D3's command, and print it;
  3. write a small file;
  4. run `cosign sign-blob --yes --signing-config … --bundle … <file>`;
  5. run `cosign verify-blob --bundle … --certificate-identity-regexp '.+github.com\/mchudinov.+' --certificate-oidc-issuer https://token.actions.githubusercontent.com --insecure-ignore-tlog=true <file>`.

`sign-blob` handles the signing config the same way `sign` does: it loads the config, drops Rekor and gets the Fulcio certificate. The proof doesn't cover writing the signature to GHCR, which this change doesn't touch.

It publishes nothing to a registry or Rekor. Fulcio puts the certificate in its public log, and the certificate names the workflow, the repo and the branch, as every release's does. The proof doesn't run locally: a browser login there would put a personal email in that log.

This is the one exception to working on `main` (D9). The branch is pushed only after asking the user, and is deleted once the result is recorded. The other workflows trigger only on `main` or on tags, so only the proof runs.

**Alternatives considered:**
- **A temporary commit and revert on `main`,** as `add-k3d-step-timeout` did for its forced hang. It leaves two commits in `main`'s history.
- **No proof.** The first run would be a release. A failure there leaves the image pushed but unsigned, and no chart published.

### D6. No CI step that rejects branch references (agreed)

No workflow runs when `release.yaml` changes, so a check wouldn't run for the file that has the downloads. A grep also can't see inside an action: it would never have caught `k3d-action`'s `install.sh`. This matches D7 of `pin-floating-action-refs`. The rule goes in `CLAUDE.md`, together with "read what an action's scripts download when adding or upgrading it".

### D7. The test cluster stays on k3d, not kind (agreed)

Moving to kind through `helm/kind-action@v1.15.2` conflicts with two scenarios of the stuck-cluster requirement:
- **"Stuck cluster fails at the limit".** kind's `--wait` timeout only logs a warning and returns success.
- **"Failed cluster leaves evidence".** A failed create deletes the node containers, and the action can't pass `--retain`.

Using kind would mean modifying that requirement and repeating the forced-hang test. kind was considered only because `k3d-action` looked unmaintained, and D1 removes that action.

The release template's "install on kind" rule means `helm install` into a throwaway cluster with no `--wait`. The k3d install meets that rule.

### D8. This change doesn't cut a release (planning default)

The branch proof covers the signing config before it lands, and the push to `main` runs the cluster steps. The next `v*` tag that's due for its own reasons runs `cosign sign` with the new config.

### D9. The work goes on `main` (agreed)

Commits go straight to `main`, as this repo does, and the user is asked before each push. The proof branch in D5 is the only exception.

## Risks / Trade-offs

- **[The first `cosign sign` with the generated config is in a release]** The proof covers the path `sign` shares with `sign-blob`, but not the write to GHCR. → If `Sign-Docker-Image` fails, the image is pushed but unsigned, and no chart is published. Recover by switching to D4 and pushing the next patch tag, as `CLAUDE.md` already describes.
- **[A k3d upgrade also needs a new hash]** → The version and the hash sit together in one step, and a mismatch fails `Build-Helm` on the push.
- **[The hash trusts the release as it was on 2026-11-10]** It came from that release's `checksums.txt`. → From now on, only that exact file runs.
- **[Signing still needs Sigstore's TUF repository]** → It's no new dependency: `cosign verify` in the same jobs already uses it.
- **[The proof's certificate is public]** → It names the workflow, the repo and the branch, as every release's certificate does.
- **[Polaris still runs beside the release package]** Its installer is a Docker action with access to Docker, and its binary has no checksum. → Out of scope. A package seal is a separate change.

## Migration Plan

1. Run the branch proof (D5), record its result, and delete the branch. If the proof fails, use D4 in task group 3.
2. Commit the cluster steps, the signing-config steps and `CLAUDE.md` on `main`. Ask the user, and push. `Build-Helm` runs because its workflow file changed, and that run proves the cluster steps. The change to `release.yaml` triggers nothing.
3. On the next `v*` tag, `Sign-Docker-Image` and `Sign-Helm-Chart` run the new step.

**Rollback:** revert the commit. Nothing that's already published depends on it.

## Open Questions

- **Does `cosign verify-blob` need `--use-signed-timestamps`** to accept the timestamp in the proof's bundle? If so, the proof adds it. Release's `cosign verify` is a different command and doesn't change.

  *Answered (2026-11-10, run 42909961721):* no. `verify-blob` printed `Verified OK` without it.
