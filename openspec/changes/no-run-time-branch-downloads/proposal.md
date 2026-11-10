## Why

Two steps download a file from another repository's `main` branch while they run, so what they do can change with no commit in this repo:
- `AbsaOSS/k3d-action@v2` pipes k3d's `install.sh` from `main` into bash in every chart check run (its `run.sh:27,68`).
- Both signing jobs `curl` Sigstore's signing config from `sigstore/root-signing`'s `main` (`release.yaml:132-135` and `:208-211`).

`pin-floating-action-refs` left both in place (its design D5). It reasoned that the chart checks hold only a read-only token, so a bad download "could fake a passing audit but couldn't publish or sign". Since `single-release-workflow` (6fcf9d6), that no longer holds. `Check-Helm` packages the chart that Release publishes and signs, and k3d's script now runs on the same runner as that package before it's uploaded. The script also broke a run once (k3d-io/k3d#1653).

## What Changes

- **Test cluster, in `Build-Helm`.** `AbsaOSS/k3d-action@v2` is replaced by two steps:
  - The first downloads `k3d-linux-amd64` v5.9.0 from k3d's release into `$RUNNER_TEMP` and checks it against a sha256 written in the workflow.
  - The second, which keeps `id: k3d` and `timeout-minutes: 5`, runs `k3d cluster create test-cluster --agents 1 --wait` and then waits for every node to be Ready.

  The k3d version, the cluster and the diagnostics step stay the same. The comment about `install.sh` goes.
- **Signing config, in both signing jobs of `Release`.** `curl … | jq 'del(.rekorTlogUrls)'` is replaced by `cosign signing-config create --with-default-services --no-default-rekor`.
  - cosign reads the services from Sigstore's TUF repository and verifies them before use.
  - On 2026-11-10, the generated file named the same Fulcio, OIDC and timestamp services as today's file, with no Rekor.
  - If the proof below fails, the fallback keeps the download but pins it to commit `aa440a9b8612b7d016d6052468e7d5703948f26c` and checks its sha256.
- **Proof before release.** Before the change lands on `main`, a temporary workflow on a throwaway branch signs a file with the generated config and verifies it the way Release does. It publishes nothing to a registry. The branch is deleted afterwards.
- **`CLAUDE.md`.** The rule about branch references extends to files downloaded at run time. It also says to read what an action's scripts download when adding or upgrading one.

**Out of scope:**
- Protecting the chart package from the other steps that run after it's packaged in `Check-Helm`. The Polaris installer and its binary, which is downloaded without a checksum, still run there. Neither comes from a branch.
- Signing the chart by digest instead of by tag.
- A check that the chart's rendered image is the one releases publish.
- A CI step that rejects branch references, and moving the test cluster to kind. See design D6 and D7.
- Moving major-version tags such as `actions/checkout@v7` to exact versions or SHAs.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: "Third-party actions are never referenced by a branch" changes in two ways.
  - It covers files that workflow steps and the actions they use download while they run, which it currently excludes.
  - The tool that creates the test cluster must be the one whose checksum the chart workflow names.

## Impact

- **CI:** the cluster step in `.github/workflows/build-helm.yaml`, and the `Create signing config without Rekor` step in both signing jobs of `.github/workflows/release.yaml`. `AbsaOSS/k3d-action` is no longer used.
- **Docs:** `CLAUDE.md`.
- **Temporary:** one workflow file on a throwaway branch, removed with the branch after the proof.
- **Unchanged:**
  - the application code, the tests, `Dockerfile` and the chart;
  - the k3d and cosign versions;
  - which Sigstore services sign, and what a release publishes.

  With no C# or project file changes, all the work stays in the main session.
- **When it takes effect:**
  - The cluster steps run on the push to `main`, because `Build-Helm` triggers on its own workflow file.
  - The signing-config step reaches `cosign sign` first on the next `v*` tag. The branch proof runs it with `cosign sign-blob` before that.
