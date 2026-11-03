## 1. Workflow change (main session, on `main`, design D1 to D3)

- [x] 1.1 In `.github/workflows/build-helm.yaml`, add `if: github.event_name == 'workflow_run'` to the `Build-and-Push-Helm` job, with a one-line comment: only a release publishes, and other triggers only check. Leave the triggers, `Check-Helm` (including its `if:`) and `Sign-Helm-Chart` unchanged.

  Verify:
  - `git diff .github/workflows/build-helm.yaml` shows only the new `if:` line and its comment, inside `Build-and-Push-Helm`.
  - `Select-String -Path .github/workflows/build-helm.yaml -Pattern '^\s+if:'` lists exactly two conditions: `Check-Helm`'s existing one and the new one.
- [ ] 1.2 Lint the workflows by running `docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:latest` from the repository root. Verify:
  - It reports no findings on the lines added in 1.1.
  - The only findings are the pre-existing `SC2086` notes in `build-docker.yaml` "Catch Docker image version" and `build-helm.yaml` "Extract chart info", recorded in the archived `migrate-tests-to-mstest-sdk` task 3.4.

  If Docker isn't running, record this as **not run**, with the error.

  **Not run** (2026-11-03): the Docker Desktop engine wasn't running (`failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`), and there is no local `actionlint` or Go toolchain. The change is one `if:` with a plain `==` expression. Tasks 3.2 and 4.1 test it live. Re-run this once Docker is up.

## 2. Documentation (main session)

- [x] 2.1 In `CLAUDE.md`, under "CI/CD and versioning", rewrite item 3 (**Build-Helm**) to say:
  - It runs after `Build-Docker` completes, on pushes and PRs to `main` touching `helm/**`, and on manual runs. Every run lints the chart, installs it into k3d and runs the Polaris audit.
  - Only a run that follows a successful `Build-Docker` pushes the chart to GHCR and signs it. Pushes, PRs and manual runs publish nothing, so a released chart version is never overwritten.
  - If a release's chart publishing fails, fix the cause and push the next patch tag. A manual run can't publish.
  - Keep the existing notes: don't rename `Build-Docker`; the Polaris thresholds.

  In the Commands block, change the comment `the README/build-helm.sh form \`helm lint --strict\` fails` to name only the README, because 3.1 fixes the script.

  Verify:
  - `Select-String -Path CLAUDE.md -Pattern 'Build-Helm'` shows the new wording.
  - `Select-String -Path CLAUDE.md -Pattern 'build-helm.sh'` returns nothing.
- [x] 2.2 Commit 1.1 and 2.1 together on `main`. Verify that `git show --stat HEAD` lists only `.github/workflows/build-helm.yaml` and `CLAUDE.md`. **Ask the user before pushing.**

  After the push, verify that `gh run list --commit <sha>` shows no run. Neither file matches `helm/**` or the `Build-Dotnet` paths.

  *Observed (2026-11-03):*
  - Commit `0efab1b` changes only the two files. The user approved the push, which went up on its own as `55671a3..0efab1b`.
  - `gh run list --commit` showed no runs, both 20 s later and again after the next push.

## 3. First live check: a `helm/**`-only push (design D4)

- [x] 3.1 In `helm/build-helm.sh`, change `helm lint --strict` to `helm lint . --strict`. Change nothing else.

  Verify:
  - Run `helm lint . --strict` from `helm/` and confirm it passes.
  - `git diff --stat` lists only `helm/build-helm.sh`.
  - `Select-String -Path helm/.helmignore -Pattern '^build-helm.sh$'` matches, so the packaged chart is unchanged.

  Commit it on its own on `main`.
- [x] 3.2 Push the 3.1 commit (**ask the user first**). Verify against that commit's SHA:
  - One `Build-Helm` run with event `push` exists.
  - `gh run view <id> --json jobs --jq '.jobs[] | "\(.name) \(.conclusion)"'` shows `Check-Helm success`, and `Build-and-Push-Helm` and `Sign-Helm-Chart` as `skipped`. This covers the spec scenario "Chart change pushed to main is checked, not published".
  - `gh run list --workflow Build-Dotnet --commit <sha>` shows no run.

  *Observed (2026-11-03):*
  - Commit `2d3dc12` changes only `helm/build-helm.sh`. It was pushed on its own as `0efab1b..2d3dc12`.
  - It started exactly one run: `Build-Helm` run 41658988349, event `push`, conclusion `success`. Its jobs:
    - `Check-Helm`: `success`;
    - `Build-and-Push-Helm`: `skipped`;
    - `Sign-Helm-Chart`: `skipped`.
  - No `Build-Dotnet` run started. The newest `Build-Dotnet` runs are still those for `d4f63c6`.
- [x] 3.3 Record the 3.2 result in the archived change: in `openspec/changes/archive/2026-11-03-migrate-tests-to-mstest-sdk/tasks.md`, mark 6.3 `- [x]` and add an *Observed* note with the commit SHA and `Build-Helm` run id, and the fact that no `Build-Dotnet` run started. Verify that the archived file now has only 6.2 and 6.6 open: two `- [ ]` lines.

## 4. Manual run check

- [x] 4.1 Run `gh workflow run Build-Helm --ref main` and wait for it to finish. Verify that the job list shows `Check-Helm success`, and `Build-and-Push-Helm` and `Sign-Helm-Chart` as `skipped`. This covers the spec scenario "Manual chart run is checked, not published".

  *Observed (2026-11-03):* `Build-Helm` run 41659181770 ran at `2d3dc12` with event `workflow_dispatch` and conclusion `success`. Its jobs:
  - `Check-Helm`: `success`;
  - `Build-and-Push-Helm`: `skipped`;
  - `Sign-Helm-Chart`: `skipped`.

  **Can't be observed:** the pull-request scenario, because this repository takes no PRs (user decision, 2026-11-03). The condition treats `pull_request` the same as `push` and `workflow_dispatch`, so 3.2 and 4.1 cover it structurally.

## 5. Release check (on the next `v*` tag; record as **not run** until it happens)

- [ ] 5.1 On the next `v*` tag, verify:
  - `Build-Docker` succeeds.
  - The `Build-Helm` run it starts (event `workflow_run`) shows `Check-Helm`, `Build-and-Push-Helm` and `Sign-Helm-Chart` all `success`.
  - The chart version printed by `Check-Helm` equals the tag version.

  This covers the spec scenario "Release run still publishes the chart".

  **Pending at archive** (2026-11-03): no tag has been pushed since `0efab1b`. The user chose to archive anyway. Structural evidence:
  - The new condition `github.event_name == 'workflow_run'` matches the event of the last release's `Build-Helm` run (41640709665, for `v1.0.1`), which published and signed the chart.
  - The `needs` chain from `Check-Helm` to `Build-and-Push-Helm` to `Sign-Helm-Chart` is unchanged.

  Check this on the next tag.
