All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main` (design D8). Ask the user before the push.

Run commands from the repo root in Git Bash.

## 1. Confirm the upstream facts

- [x] 1.1 Verify that `gh api repos/sigstore/cosign-installer/commits/v4.1.3 --jq .sha` returns `756fe2d284c7292c4e9b62c8a28f1e5629c59532`, and that `gh api repos/sigstore/cosign-installer/releases/tags/v4.1.3 --jq .immutable` returns `true` (design D2). Run `gh api repos/sigstore/cosign-installer/compare/v4.1.3...main --jq .status` and record the result. If it isn't `identical`, the pin is still right, but it no longer matches what has been running, so tell the user before continuing.

  *Result (2026-11-09):* the SHA matches, `immutable` is `true`, and the comparison with `main` is `identical`.
- [x] 1.2 For each of `action.yaml`, `Dockerfile` and `get_polaris.sh` in `.github/actions/setup-polaris`, fetch the file at `v10.2.6` and at `master` with `gh api -H "Accept: application/vnd.github.raw"` and diff them. Verify that all three are identical. This replaces the inference about `action.yaml` in design Context. If any differ, stop and ask the user (design D3).

  *Result (2026-11-09):* all three are identical (384, 290 and 646 bytes).

## 2. Pin the references

- [x] 2.1 In `.github/workflows/build-docker.yaml:101` and `.github/workflows/build-helm.yaml:177`, change the reference to `sigstore/cosign-installer@v4.1.3`. Verify by reading both lines (spec: "Signing tool installer can't change without a commit").

  *Result (2026-11-09):* both lines read `sigstore/cosign-installer@v4.1.3`. After the comment added in task 2.2, the second is at `build-helm.yaml:178`.
- [x] 2.2 In `.github/workflows/build-helm.yaml:120`, change the reference to `FairwindsOps/polaris/.github/actions/setup-polaris@v10.2.6`. Above the `Setup Polaris` step, add the comment `# The ref only picks this installer. POLARIS_VERSION picks the binary it downloads, so the two can differ.` Leave `POLARIS_VERSION` at `8.5.5` (design D4). Verify by reading the step.

  *Result (2026-11-09):* the comment is at line 119 and the reference at line 121. `POLARIS_VERSION` still reads `8.5.5`.
- [x] 2.3 Verify that `grep -rnE 'uses:.*@(main|master)\b' .github/workflows` finds nothing (spec: "Workflows reference no branches"), that both `signing_config.v0.2.json` curl lines are unchanged (design D5), and that `git diff --stat .github/workflows` shows two files with 4 insertions and 3 deletions.

  *Result (2026-11-09):* the grep finds nothing, neither curl line appears in the diff, and the stat is 2 files, 4 insertions, 3 deletions.

## 3. Docs

- [x] 3.1 In `CLAUDE.md`, extend the closing paragraph of the CI/CD section (line 150) with: "Reference third-party actions by a release tag or a commit SHA, never a branch. The `setup-polaris` ref only picks the installer; `POLARIS_VERSION` picks the binary." Verify by reading the section.

  *Result (2026-11-09):* line 150 ends with both sentences, and the diff is one line.

## 4. Validate and check in CI

- [x] 4.1 Verify that `openspec validate pin-floating-action-refs --strict` passes.

  *Result (2026-11-09):* `Change 'pin-floating-action-refs' is valid`.
- [ ] 4.2 Commit on `main`. Ask the user, and push. Verify with `gh run list --commit <sha>` and the run's jobs that `Build-Helm` succeeded, including `Setup Polaris` and `Use Polaris`, that `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped, and that `Build-Docker` didn't run. In the `Setup Polaris` log, verify that the action resolved to `v10.2.6` and that the download URL names `8.5.5` (spec: "Chart audit uses the version the workflow names"). If the run fails, stop and tell the user.
- [ ] 4.3 Tell the user that the cosign pins haven't run, and that the next `v*` tag is their first run (design D6, Migration Plan). Verify by recording here that this change checked them only by reading the lines and by task 1.1, and did not cut a release.
