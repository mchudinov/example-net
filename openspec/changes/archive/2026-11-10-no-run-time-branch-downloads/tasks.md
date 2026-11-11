All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main`. The one exception is the proof branch in group 2 (design D5, D9). Ask the user before every push.

Run commands from the repo root in Git Bash.

## 1. Clean working tree and upstream facts

- [x] 1.1 The archive of `single-release-workflow` (2026-11-10) is still uncommitted: `git status` shows its deleted change files, a modified `openspec/specs/ci-test-gate/spec.md` and an untracked `openspec/changes/archive/2026-11-10-single-release-workflow/`. Ask the user to commit it separately first, or to say how to handle it. Don't fold it into this change's commits. Verify that `git status --short` then lists only `openspec/changes/no-run-time-branch-downloads/`.

  *Result (2026-11-10):* the user chose to have it committed now and pushed later with this change. It's commit `86bba26` "Archive single-release-workflow", which staged only those paths. `git status --short` then listed only `openspec/changes/no-run-time-branch-downloads/`.
- [x] 1.2 Verify that `curl -fsSL https://github.com/k3d-io/k3d/releases/download/v5.9.0/checksums.txt | grep 'k3d-linux-amd64$'` prints `06d8f25bc3a971c4eb29e0ff08429b180402db0f4dec838c9eac427e296800a0  _dist/k3d-linux-amd64` (design D1). If it prints anything else, stop and tell the user: the release has changed since planning.

  *Result (2026-11-10):* it prints exactly that line.
- [x] 1.3 Verify the signing config's history (design D3, D4):
  - `curl -fsSL https://raw.githubusercontent.com/sigstore/root-signing/refs/heads/main/targets/signing_config.v0.2.json | sha256sum` prints `9711a6d5375706957a4859af31c5866a4474f81f0544f9f4b76c9c4f4c8a539c`;
  - the same URL with `aa440a9b8612b7d016d6052468e7d5703948f26c` in place of `refs/heads/main` prints the same hash;
  - `gh api "repos/sigstore/root-signing/commits?path=targets/signing_config.v0.2.json" --jq length` prints `1`.

  If `main`'s hash differs, Sigstore has changed the file, so the generated config may no longer match what signs today. Tell the user before continuing.

  *Result (2026-11-10):* both URLs hash to `9711a6d5…`, and the commit count is `1`.
- [x] 1.4 Verify that `gh api -H "Accept: application/vnd.github.raw" "repos/sigstore/cosign-installer/contents/action.yml?ref=v4.1.3"` shows `v3.1.4` as the default of the `cosign-release` input (design Context).

  *Result (2026-11-10):* `action.yml:10-13` sets `default: 'v3.1.4'` for `cosign-release`.

## 2. Prove the signing config on a throwaway branch

- [x] 2.1 Run `git switch -c tmp/signing-config-proof` from `main`. On that branch only, add and commit `.github/workflows/tmp-signing-config-proof.yaml`, as design D5 describes. Stage it by name, so this change's untracked folder stays out of the commit. Verify by reading the file:
  - its trigger names only that branch;
  - `permissions` holds only `id-token: write`;
  - it has no checkout;
  - the installer is `sigstore/cosign-installer@v4.1.3`;
  - the create command is exactly design D3's;
  - `verify-blob` takes the same identity regexp, OIDC issuer and `--insecure-ignore-tlog=true` as Release's `Verify the signature` steps.

  *Result (2026-11-10):* commit `121519d` on the branch, staging only that file. All six points hold. The identity regexp is Release's `'.+github.com\/${{ github.repository_owner }}.+'`, and the issuer is the literal value of Release's `OIDC_ISSUER`.
- [x] 2.2 Ask the user, then run `git push -u origin tmp/signing-config-proof`. With `gh run list --branch tmp/signing-config-proof`, verify that only the proof workflow ran. With `gh run view <id> --log`, verify that:
  - the printed config lists `https://fulcio.sigstore.dev`, `https://oauth2.sigstore.dev/auth` and `https://timestamp.sigstore.dev/api/v1/timestamp`, and has no `rekorTlogUrls`;
  - `sign-blob` wrote the bundle without uploading to a transparency log;
  - `verify-blob` printed `Verified OK`.

  If `verify-blob` fails only because it needs `--use-signed-timestamps` (design Open Questions), add that flag, push again, and record it. If anything else fails, stop and tell the user. Group 3 then follows design D4.

  *Result (2026-11-10):* the user approved the push. Run 42909961721, `TMP-Signing-Config-Proof`, was the only run on the branch, and every step succeeded.
  - The printed config's URLs are exactly `https://fulcio.sigstore.dev`, `https://oauth2.sigstore.dev/auth` and `https://timestamp.sigstore.dev/api/v1/timestamp`. It has no `rekorTlogUrls`.
  - `sign-blob` printed `Wrote bundle to file …/blob.sigstore.json`, and the log has no `tlog entry created` line and no Rekor URL.
  - `verify-blob` printed `Verified OK`, after cosign's usual warning about skipping tlog verification.

  `--use-signed-timestamps` wasn't needed, which answers the design's open question. The log's only `raw.githubusercontent.com` lines are cosign-installer printing its script, with the key URL at `${input_cosign_release}`, a release tag. Group 3 uses design D3.
- [x] 2.3 Run `git switch main`, then `git push origin --delete tmp/signing-config-proof` and `git branch -D tmp/signing-config-proof`. Verify that `git ls-remote --heads origin tmp/signing-config-proof` prints nothing, and that `git branch --list 'tmp/*'` is empty. Record the run ID and the result of task 2.2 here.

  *Result (2026-11-10):* the branch was deleted on origin and locally (it was `121519d`). Both checks print nothing, and `.github/workflows/` on `main` has no `tmp-signing-config-proof.yaml`. The proof was run 42909961721, which passed (task 2.2).

## 3. Build the signing config with cosign

If task 2.2 failed and the user chose design D4, use D4's run lines in tasks 3.1 and 3.2 instead: `curl -fsSL` of the URL pinned to `aa440a9…`, a `sha256sum -c` check against `9711a6d5…`, then `jq 'del(.rekorTlogUrls)'`.

- [x] 3.1 In `.github/workflows/release.yaml`, find the `Create signing config without Rekor` step in the `Sign-Docker-Image` job (lines 132-135).
  - Replace its `run:` with `cosign signing-config create --with-default-services --no-default-rekor --out "$RUNNER_TEMP/signing-config-no-tlog.json"`.
  - Above the step, add the comment `# cosign takes the services from Sigstore's TUF repository and verifies them. Without Rekor, nothing goes to the transparency log.`

  Verify by reading that the step still follows `Install cosign`, and that `Sign the image` still passes `--signing-config "$RUNNER_TEMP/signing-config-no-tlog.json"`.

  *Result (2026-11-10):* the comment is at line 132, and the step at lines 133-134 follows `Install cosign`. `Sign the image` still passes that `--signing-config`. The old line 135's trailing spaces went with it.
- [x] 3.2 Make the same change to the step in the `Sign-Helm-Chart` job (lines 208-211), at that job's indentation. Verify by reading the step, and that `grep -nE 'raw\.githubusercontent|jq ' .github/workflows/release.yaml` finds nothing (spec: "Nothing is downloaded from a branch").

  *Result (2026-11-10):* the comment and step sit at lines 207-209, at the job's six-space step indentation, after `Install cosign` and before `Sign the chart`. The grep finds nothing.

## 4. Install k3d from its release

- [x] 4.1 In `.github/workflows/build-helm.yaml`, replace the `Create a test k3d cluster for dry-run install` step and its comment (lines 109-119) with two steps:
  - **`Install k3d`.** It has no `id`, and sets `env:` `K3D_VERSION: v5.9.0` and `K3D_SHA256: 06d8f25bc3a971c4eb29e0ff08429b180402db0f4dec838c9eac427e296800a0`. It runs:
    - `curl -fsSL --retry 3 -o "$RUNNER_TEMP/k3d" "https://github.com/k3d-io/k3d/releases/download/$K3D_VERSION/k3d-linux-amd64"`;
    - `echo "$K3D_SHA256  $RUNNER_TEMP/k3d" | sha256sum -c -`;
    - `sudo install "$RUNNER_TEMP/k3d" /usr/local/bin/k3d`.

    Above it, add the comment `# k3d's releases aren't immutable, so the checksum is written here rather than read from the release. Bump both values together.`
  - **`Create a test k3d cluster for dry-run install`.** It has `id: k3d` and `timeout-minutes: 5`, and runs `k3d cluster create test-cluster --agents 1 --wait` and then `kubectl wait --for=condition=Ready nodes --all --timeout=5m` (design D2). Keep the existing comment above it: `# k3d waits for its nodes with no limit of its own. The timeout kills only the k3d client, so the containers stay for the next step.`

  Drop the comment about `install.sh` and k3d-io/k3d#1653. Verify by reading that the diagnostics step is unchanged and still tests `steps.k3d.outcome`, and that `grep -nE 'k3d-action|install\.sh' .github/workflows/build-helm.yaml` finds nothing.

  *Result (2026-11-10):* `Install k3d` is at lines 109-117 and the cluster step at lines 119-125, each with its comment. The diagnostics step follows them unchanged, still testing `steps.k3d.outcome == 'failure'`. The grep finds nothing.
- [x] 4.2 Test the check line from task 4.1 locally, in a new folder in the scratchpad:
  - Download v5.9.0's `k3d-linux-amd64` there, run the check line against it with `K3D_SHA256` and `RUNNER_TEMP` set, and verify that it prints `OK`.
  - Append one byte to a copy, and verify that the same check exits non-zero.

  This covers the spec scenario "Cluster tool that doesn't match its checksum fails the check".

  *Result (2026-11-10):* in Git Bash, the downloaded binary gave `…/good/k3d: OK` with exit 0. The copy with one byte appended gave `…/bad/k3d: FAILED` and `WARNING: 1 computed checksum did NOT match`, with exit 1. A step's default shell is `bash -e`, so the step stops there, before `sudo install`.

## 5. Docs

- [x] 5.1 In `CLAUDE.md`, edit the closing paragraph of the CI/CD section (line 151).
  - Replace "Reference third-party actions by a release tag or a commit SHA, never a branch." with "Reference third-party actions by a release tag or a commit SHA, never a branch, and download nothing from a branch while a workflow runs. When adding or upgrading an action, read what its scripts download."
  - Add "k3d comes from its release binary, checked against `K3D_SHA256` in `build-helm.yaml`, so bump `K3D_VERSION` and `K3D_SHA256` together."

  Verify by reading the paragraph.

  *Result (2026-11-10):* line 151 has the new branch-and-download sentence, the "read what its scripts download" sentence and the k3d sentence. The `setup-polaris` sentence sits between them, unchanged.
- [x] 5.2 In `CLAUDE.md`'s Release list (line 133), change "sign it with cosign (keyless, no Rekor);" to "sign it with cosign (keyless, no Rekor; cosign builds the signing config from Sigstore's TUF repository);". Verify by reading the item.

  *Result (2026-11-10):* line 133 reads as specified. `git diff --stat -- CLAUDE.md` shows 2 insertions and 2 deletions.

## 6. Validate and check in CI

- [x] 6.1 Verify that `openspec validate no-run-time-branch-downloads --strict` passes.

  *Result (2026-11-10):* `Change 'no-run-time-branch-downloads' is valid`.
- [x] 6.2 Verify that `git status --short` lists only `.github/workflows/release.yaml`, `.github/workflows/build-helm.yaml`, `CLAUDE.md` and this change's folder. Also verify that `.github/workflows/` has no `tmp-signing-config-proof.yaml`. Then verify with `grep -rhoE 'uses:[[:space:]]*[^./][^[:space:]#]+' .github/workflows/ | sort -u` that the only difference from the list scanned in design Context is that `AbsaOSS/k3d-action@v2` is gone (spec: "Nothing is downloaded from a branch").

  *Result (2026-11-10):* `git status --short` lists the two workflows and `CLAUDE.md` as modified, and this change's folder as untracked. `.github/workflows/` has no `tmp-signing-config-proof.yaml`. Diffing the `uses:` list at `HEAD` against the working tree shows one line, `< AbsaOSS/k3d-action@v2`. The other ten third-party references are the ones scanned in design Context. The grep also matches the two local `./.github/workflows/…` calls, through the space after `uses:`, and they appear on both sides.
- [x] 6.3 Lint both changed workflows locally, because nothing parses `release.yaml` until a tag is pushed, and `Sign-Helm-Chart` sits two spaces deeper than `Sign-Docker-Image`.
  - Download `actionlint` v1.7.12's `actionlint_1.7.12_windows_amd64.zip` and `actionlint_1.7.12_checksums.txt` into a new scratchpad folder.
  - Check the zip with `sha256sum -c` against its line, and unzip it.
  - Run it on `.github/workflows/release.yaml` and `.github/workflows/build-helm.yaml`, both before the edits (with `git stash`) and after them.

  Verify that the run after the edits reports no finding that the run before them didn't.

  *Result (2026-11-10):* the zip matched its checksum line. The baseline ran before the edits, without `git stash`, because no workflow had been edited yet. Both runs exited 0 with no output, so there are no findings before or after.
- [x] 6.4 Commit on `main`, staging `.github/workflows/release.yaml`, `.github/workflows/build-helm.yaml`, `CLAUDE.md` and `openspec/changes/no-run-time-branch-downloads/` by name. Ask the user, and push. Verify with `gh run list --commit <sha>` that `Build-Helm` ran and succeeded, and that `Check-Docker` and `Build-Dotnet` didn't run. In the `Build-Helm` log, verify that:
  - `Install k3d` printed `OK`;
  - `Create a test k3d cluster for dry-run install` shows `condition met` for every node;
  - the diagnostics step was skipped;
  - both test installs passed;
  - no `raw.githubusercontent.com` URL appears anywhere in the log.

  If the run fails, stop and tell the user.

  *Result (2026-11-10):* commit `08433d3`, pushed together with `86bba26` after the user approved. Run 42910555875 was the only run for either commit. `Build-Helm` / `Check-Helm` succeeded, and `Check-Docker` and `Build-Dotnet` didn't run.
  - `Install k3d` printed `/home/runner/work/_temp/k3d: OK`.
  - The cluster step logged `Cluster 'test-cluster' created successfully!`, then `condition met` for `k3d-test-cluster-agent-0` and `k3d-test-cluster-server-0`.
  - The diagnostics step was skipped.
  - Both test installs, the label checks, Polaris and the upload succeeded.
  - The run's log has no `raw.githubusercontent.com` line.
- [x] 6.5 Tell the user that `cosign sign` hasn't yet run with the new config on an image or a chart, and that the next `v*` tag is its first run (design D8, Risks). Verify by recording here that this change proved the config only with `sign-blob` (task 2.2), and did not cut a release.

  *Result (2026-11-10):* the user was told in the closing summary. This change proved the generated config only with `sign-blob` and `verify-blob`, in run 42909961721 (task 2.2), and did not cut a release.
