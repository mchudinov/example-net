## 1. The `Check-Docker` workflow (main session, design D1, D4, D5)

- [x] 1.1 Create `.github/workflows/check-docker.yaml`:
  - `name: Check-Docker`
  - **Triggers** (design D4): `workflow_dispatch`, plus `push` and `pull_request` on `branches: [main]`, each with the same explicit `paths` list:
    - `Dockerfile`
    - `.dockerignore`
    - `global.json`
    - `src/**`
    - `.github/workflows/check-docker.yaml`
  - **Permissions:** top-level `permissions: contents: read`.
  - **One job, `Build-Image`, on `ubuntu-latest`,** with two steps:
    1. `actions/checkout@v7`;
    2. `docker/build-push-action@v7` with `context: .`, `push: false` and `tags: example-net-docker:check`.

  Short comments should say:
  - the workflow builds only and publishes nothing;
  - it must never be named `Build-Docker`, because `Build-Helm` matches that name and publishes the chart.

  Verify:
  - Reading the file shows the name, triggers, paths, permissions and steps above.
  - The `push` and `pull_request` path lists are identical.
  - `grep -nE "login-action|setup-buildx|cache-(from|to)|push: true|packages:|id-token" .github/workflows/check-docker.yaml` returns nothing.

  *Result (2026-11-04):* the file is created as specified, with job `Build-Image`; the step is named "Build image (not pushed)".
  - **Comments:** "builds only, publishes nothing"; "never name this workflow `Build-Docker`"; and "a new root-level file the Dockerfile copies must be added to both lists".
  - **Forbidden-pattern grep:** returns nothing (exit 1).
  - **Path lists:** the `push` and `pull_request` lists are byte-identical (`diff`).
- [x] 1.2 From the root, in Git Bash, run `MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/repo" -w /repo rhysd/actionlint:latest`. Verify:
  - `check-docker.yaml` has 0 findings.
  - The only other findings are the six pre-existing `SC2086` notes in `build-docker.yaml` and `build-helm.yaml`.

  If Docker is unavailable, record this as **not run**.

  *Result:* actionlint 1.7.12 reports 0 findings in `check-docker.yaml`. The only findings are the six pre-existing `SC2086` (info) notes: three in `build-docker.yaml:54` and three in `build-helm.yaml:46`.
- [x] 1.3 Run the local equivalent of the check from the root: `docker build -t example-net-docker:check .`. Verify that it succeeds. Then run `docker image rm example-net-docker:check`.

  *Result:* the build exited 0, and the `:check` image was removed afterwards.
- [x] 1.4 Check the failure case locally (spec scenario "Broken image build fails the check") without touching the repository's Dockerfile.
  1. Write a broken copy to the session scratchpad: `sed 's#src/ExampleNet/ExampleNet.csproj src/ExampleNet/#src/Missing/Missing.csproj src/Missing/#' Dockerfile > <scratchpad>/Dockerfile.broken`.
  2. Run `docker build -f <scratchpad>/Dockerfile.broken .` from the root.

  Verify:
  - The build exits non-zero. `build-push-action` fails its job on the same error.
  - `git status --porcelain Dockerfile` is empty.

  *Result:* the broken copy's line 9 reads `COPY src/Missing/Missing.csproj src/Missing/`. The build exited 1 with `ERROR: failed to build: failed to solve: … "/src/Missing/Missing.csproj": not found`. `git status --porcelain Dockerfile` is empty.
- [x] 1.5 Verify that `git diff --stat` shows no change to these files:
  - `.github/workflows/build-docker.yaml`
  - `.github/workflows/build-dotnet.yaml`
  - `.github/workflows/build-helm.yaml`
  - `Dockerfile`
  - `.dockerignore`
  - `global.json`
  - `helm/`

  *Result:* `git diff --stat HEAD` over those paths is empty. The working tree shows only the new `check-docker.yaml` and this change's planning files.

## 2. Documentation and commit (main session, design D7)

- [x] 2.1 Update `CLAUDE.md`:
  - **CI/CD and versioning:** add a `Check-Docker` entry. It covers the triggers and paths, that it only builds (`push: false`, read-only permissions, no login), that it runs in parallel with `Build-Dotnet`, and that it must never be named `Build-Docker`, because `Build-Helm` would then publish the chart.
  - **Container build note:** extend the rule about root-level build files. Such a file needs its own `COPY` in the Dockerfile **and** an entry in `Check-Docker`'s paths.

  Verify:
  - `grep -n "Check-Docker" CLAUDE.md` finds both places.
  - `CLAUDE.md` keeps its CRLF line endings. Use the Edit tool, not `sed -i`.

  *Result:*
  - **New CI/CD entry:** `Check-Docker` is item 2, between Build-Dotnet and Build-Docker; Build-Docker and Build-Helm renumbered to 3 and 4. It lists the triggers and paths and that it only builds (`push: false`, read-only, no login, no cache). It says it runs in parallel with Build-Dotnet, why `tests/**` and `helm/**` don't trigger it, and "Never name it `Build-Docker`".
  - **Container build note:** the root-level build-file rule now ends with "Add the file to `Check-Docker`'s `paths` as well".
  - **Checks:** `grep -n "Check-Docker" CLAUDE.md` finds both places (lines 73 and 112). CRLF line endings are kept. `git diff --stat` shows 18 insertions and 3 deletions.
- [x] 2.2 Once the user approves, commit tasks 1.1 and 2.1, plus this change's folder if the user wants it included, on `main`. Verify:
  - `git show --stat HEAD` lists only those files.
  - `git branch --show-current` prints `main`.

  **Ask the user before pushing.**

  *Result (2026-11-04):*
  - **Approval:** the user approved committing with the change folder included, and pushing.
  - **Commit:** `ffc1343` on `main`. `git show --stat HEAD` lists only `.github/workflows/check-docker.yaml`, `CLAUDE.md` and the five files under `openspec/changes/ci-docker-build-check/`.
  - **Push:** `origin/main` was at `9b28e47` before the push, and the push sent `9b28e47..ffc1343`.
  - **Not in the commit:** this result and the edits to this file after 2.1.

## 3. Live verification (after pushing; record each check as **pending** until it is observed)

- [x] 3.1 On the push that adds the workflow, verify with `gh run list --commit <sha>` and the run log:
  - `Check-Docker` runs (its own file is in its paths) and succeeds.
  - No `Build-Docker` or `Build-Helm` run belongs to that commit.
  - The build log shows no registry push.

  *Observed (2026-11-04):* `Check-Docker` run 41881893007 (event `push`, `ffc1343`) succeeded. Job `Build-Image` passed checkout and "Build image (not pushed)".
  - **Builder:** the log shows `building with "default" instance using docker driver`, the same builder as `Build-Docker`. The only compiler warning is the baseline CS8605.
  - **Output:** the image ended as `naming to docker.io/library/example-net-docker:check`, a local name.
  - **No push:** the log has 0 matches for `pushing layers`, `pushing manifest`, a `ghcr.io/…:` reference or `login succeeded`. The `"Direct push": true` line comes from the builder's feature list, not from anything the build did.
  - **No other runs:** `gh run list --commit <full sha>` shows only this run, and no run of any workflow has started since.

  **`gh run list --commit` needs the full SHA.** A short SHA silently returns no runs: `0459911` gives 0, its full SHA gives 1. Use `--commit "$(git rev-parse <sha>)"` in the checks below.

  **Builder capabilities:** the feature list also shows `"Cache export": true`. The runner's default builder with the containerd image store can export a cache, so a later cache change wouldn't strictly need `setup-buildx`.
- [x] 3.2 On the next push that changes the `Dockerfile`, `.dockerignore`, `global.json` or `src/**`, verify that `Check-Docker` runs and succeeds (spec scenario "Image builds for a change to its build definition").

  If the push changes only `src/**`, also verify that `Build-Dotnet` runs. That also counts as the pending task 6.2 of the archived `restructure-source-layout`.

  **Pending at archive** (2026-11-04): no push since `ffc1343` has changed these paths. The user chose to archive anyway. Structural evidence:
  - the `push` paths list exactly these files (task 1.1);
  - the first run, 3.1, already built the image successfully on the runner.

  *Observed after archive (2026-11-05):* commit `08ca2aa` changes only `src/ExampleNet/appsettings.json` (one added blank line). Its push started both workflows at 09:29:45Z, in parallel; both finished at 09:30:18Z.
  - **`Check-Docker`:** run 42002826962 (event `push`) succeeded, job `Build-Image`.
  - **`Build-Dotnet`:** run 42002827128 (event `push`) succeeded, job `Build-and-Test`.
  - **Not evidence:** the earlier commit `79364ca` started no runs, correctly. It is an empty commit made through GitHub's web interface: its tree equals its parent's, and GitHub reports 0 changed files. So no path filter could match it.

  The scenario's wording names a change to the build definition, while this push changed application source. The trigger for `Dockerfile` and `.dockerignore` is still covered only by the `paths` list (task 1.1).
- [x] 3.3 On the next push that changes only `tests/**` or only `helm/**`, verify that **no** `Check-Docker` run is triggered (spec scenario "Change outside the image does not trigger an image build").

  **Pending at archive** (2026-11-04): no push that changes only `tests/**` or only `helm/**` has happened since `ffc1343`. Structural evidence: neither `tests/**` nor `helm/**` is in the paths (task 1.1).

  *Observed after archive (2026-11-05):* commit `f71d46e` changes only `tests/ExampleNetTests/SettingsTests.cs` (it adds a trailing newline). Its push started only `Build-Dotnet` run 42003929426, which succeeded at 09:38:04Z. `gh run list --commit <full sha> --workflow Check-Docker` returned 0 runs after that run had finished. The last `src/` push started both workflows at the same moment, so a `Check-Docker` run would have been visible by then.

  The `helm/**` half of the scenario hasn't been seen live; it remains covered by the `paths` list.
- [x] 3.4 For every `Check-Docker` run observed in 3.1 to 3.3, verify that no `Build-Helm` run follows it, and that no new image version appears for that commit (spec: the check publishes nothing).

  **Pending at archive** (2026-11-04): it holds for the only run so far, 3.1. No `Build-Helm` run followed run 41881893007, and its log shows no registry push. The task stays open until the runs in 3.2 and 3.3 are observed.

  *Update (2026-11-05):* it also holds for the 3.2 run. No `Build-Helm` run followed run 42002826962, and its log has 0 matches for `pushing layers`, `pushing manifest`, a `ghcr.io/…:` reference or `login succeeded`. The task stays open until 3.3 is observed.

  *Closed (2026-11-05):* 3.3 started no `Check-Docker` run, so the runs from 3.1 and 3.2 are the only ones this task covers. No `Build-Helm` run followed either; the latest `Build-Helm` run is still 41693317431, from before this change.

  **Limit of this evidence:** the registry itself wasn't queried, because the local `gh` token lacks `read:packages`. "No new image version" rests on the run logs showing no push, and on the workflow having read-only permissions and no registry login.
- [ ] 3.5 On the next `v*` tag, verify:
  - **No `Check-Docker` run** is started by the tag.
  - **`Build-Docker` runs as before:** the `Test` gate; build, push and sign; the bot's `Chart.yaml` bump; then `Build-Helm` publishes and signs the chart.

  This also covers the pending task 6.5 of the archived `restructure-source-layout`.

  **Pending at archive** (2026-11-04): no `v*` tag has been pushed since `ffc1343`. Structural evidence:
  - `branches: [main]` on the `push` trigger excludes tag pushes;
  - `Build-Docker` is unchanged (task 1.5).
- [ ] 3.6 Pull-request scenario ("Application change is image-build checked"): this repository takes no PRs, so record it as **not run**. Structural evidence: task 1.1 verifies that the `pull_request` trigger lists the same paths as `push`.

  **Not run** (2026-11-04): the user works directly on `main` and opens no PRs. Structural evidence: in task 1.1, `diff` showed the `pull_request` path list is byte-identical to the `push` list.
