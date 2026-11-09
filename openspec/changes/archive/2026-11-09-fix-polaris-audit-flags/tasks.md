All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main` (design D7). Ask the user before the push, and land this before the next tag.

Run commands from the repo root in Git Bash unless a task says otherwise. In these tasks:
- `<scratchpad>` is the session's scratchpad directory.
- `<polaris>` is the Polaris 8.5.5 binary from task 1.1.
- `<flags>` is `--helm-chart helm --only-show-failed-tests --format=pretty --color=false --set-exit-code-on-danger --config .github/polaris-config.yaml`.

## 1. Set up and see today's result

- [x] 1.1 Into a new directory under `<scratchpad>`, run `gh release download 8.5.5 --repo FairwindsOps/polaris` for `checksums.txt` and the archive for the local platform, and extract the archive. Verify that the archive's SHA-256 matches `checksums.txt`, and that `<polaris> version` reports `8.5.5`.

  *Result (2026-11-09):* `polaris_windows_amd64.tar.gz`, SHA-256 `b32f5262…ae1c1` matches `checksums.txt`, and the version is `8.5.5`.
- [x] 1.2 Before editing, run the step's current text as CI does, on one line with its backslashes: `<polaris> audit --helm-chart helm \ --helm-values ./helm/values.yaml \ --only-show-failed-tests true --format=pretty --color=false --set-exit-code-on-danger \ --set-exit-code-below-score 90 \ --config .github/polaris-config.yaml`. Verify `Final score: 86`, exit 0, and that passing checks are printed.

  *Result (2026-11-09):* `Final score: 86`, exit 0, and 40 passing rows printed.

## 2. Fix the step

- [x] 2.1 In `.github/workflows/build-helm.yaml`, replace the `run:` of `Use Polaris` with a block scalar, keeping the `${{ env.* }}` values and the indentation of the file's other steps:

  ```yaml
      - name: Use Polaris
        run: |
          polaris audit --helm-chart ${{ env.HELM_WORKING_DIRECTORY }} \
            --helm-values ./${{ env.HELM_WORKING_DIRECTORY }}/values.yaml \
            --only-show-failed-tests --format=pretty --set-exit-code-on-danger \
            --set-exit-code-below-score ${{ env.POLARIS_EXIT_SCORE }} \
            --config .github/polaris-config.yaml
  ```

  Verify that `git diff --stat` shows only this file, and that `POLARIS_EXIT_SCORE` still reads `90` and `POLARIS_VERSION` still reads `8.5.5` (design D2).

  *Result (2026-11-09):* the diff for this change is this one file, at 6 insertions and 5 deletions. The stat also lists `pin-floating-action-refs/tasks.md`, which was already modified and isn't part of this change. `POLARIS_EXIT_SCORE` reads `90` and `POLARIS_VERSION` reads `8.5.5`.
- [x] 2.2 Copy the step's body from the workflow file into `<scratchpad>/step.sh`, substituting `helm` for `${{ env.HELM_WORKING_DIRECTORY }}` and `90` for `${{ env.POLARIS_EXIT_SCORE }}`. Run it with `bash -e` from the repo root with `<polaris>`'s directory on `PATH`. Verify `Final score: 94`, exit 0, no `Danger` finding, and no passing checks printed (spec: "Chart that meets the policy passes").

  *Result (2026-11-09):* `Final score: 94`, exit 0, no `Danger` rows, no passing rows, and 4 `Warning` rows. A copy of the step with the threshold at 95 exited 4 and didn't continue past the audit, so `bash -e` stops the step.

## 3. Prove the gate can fail

- [x] 3.1 Run `<polaris> audit <flags> --helm-values ./helm/values.yaml --set-exit-code-below-score 95`. Verify exit 4 and the message that 94 is less than the minimum of 95 (spec: "Score below the threshold fails the check").

  *Result (2026-11-09):* exit 4, with `Audit score of 94 is less than the provided minimum of 95`.
- [x] 3.2 In `<scratchpad>`, copy `helm/values.yaml` and set `allowPrivilegeEscalation: true`. Run `<polaris> audit <flags> --helm-values <copy> --set-exit-code-below-score 90`. Verify exit 3 and that the output names `privilegeEscalationAllowed` as `Danger` (spec: "Danger finding fails the check").

  *Result (2026-11-09):* exit 3, `Final score: 91`, `privilegeEscalationAllowed Danger`, and `1 danger items found in audit`.
- [x] 3.3 Run `<polaris> audit --helm-chart helm --helm-values ./helm/values.yaml --format=json --config .github/polaris-config.yaml`. Verify that no result has the check `missingNetworkPolicy`, and that `linuxHardening` has severity `danger` (spec: "Repository configuration decides the checks").

  *Result (2026-11-09):* 45 results, none for `missingNetworkPolicy`, and `linuxHardening` is `danger` on both the Deployment and the test Pod. The five failing results are the warnings listed in design Context.
- [x] 3.4 Verify by reading `.github/workflows/build-helm.yaml` that `Build-and-Push-Helm` needs `Check-Helm` and `Sign-Helm-Chart` needs both (spec: "Failed audit blocks publishing"). Record that tasks 3.1 to 3.4 are local evidence and that the failing cases weren't run on live CI (design Risks).

  *Result (2026-11-09, local evidence, failing cases not run on live CI):* line 134 reads `needs: Check-Helm`, and line 163 reads `needs: [Check-Helm, Build-and-Push-Helm]`.

## 4. Validate and check in CI

- [x] 4.1 Verify that `openspec validate fix-polaris-audit-flags --strict` passes.

  *Result (2026-11-09):* `Change 'fix-polaris-audit-flags' is valid`.
- [x] 4.2 Commit on `main`, staging only `.github/workflows/build-helm.yaml` and this change's folder, because `pin-floating-action-refs/tasks.md` may have an uncommitted edit. Ask the user, and push. Verify with `gh run list --commit <sha>` and the run's jobs that `Build-Helm` succeeded, that `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped, and that `Build-Docker` didn't run. In the `Use Polaris` log, verify `Final score: 94`, the command with no backslashes in it, and no passing checks printed. If the run fails or the score differs, stop and tell the user, and don't tag.

  *Result (2026-11-09):* commit `b67dcb9`, run 42656239890, the only run for that commit. Attempt 1 hung in the k3d step on `Starting node 'k3d-test-cluster-agent-0'` for 2 h 43 min, and the user had it cancelled. Up to that line its log matches the last passing run (runner image, k3d checksum, k3s image), and this change doesn't touch that step. The cause of the hang is unknown. Attempt 2 succeeded, with the k3d step taking 35 s. `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped, and `Build-Docker` didn't run. `Use Polaris` shows `Final score: 94`, four `Warning` rows and no passing rows. The command Polaris received has no backslashes, and the script lines in the log keep them only as line continuations. The runner used the Linux binary with Helm 4.3.1, so the local result holds there.
