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
- [ ] 2.2 Once the user approves, commit tasks 1.1 and 2.1, plus this change's folder if the user wants it included, on `main`. Verify:
  - `git show --stat HEAD` lists only those files.
  - `git branch --show-current` prints `main`.

  **Ask the user before pushing.**

## 3. Live verification (after pushing; record each check as **pending** until it is observed)

- [ ] 3.1 On the push that adds the workflow, verify with `gh run list --commit <sha>` and the run log:
  - `Check-Docker` runs (its own file is in its paths) and succeeds.
  - No `Build-Docker` or `Build-Helm` run belongs to that commit.
  - The build log shows no registry push.
- [ ] 3.2 On the next push that changes the `Dockerfile`, `.dockerignore`, `global.json` or `src/**`, verify that `Check-Docker` runs and succeeds (spec scenario "Image builds for a change to its build definition").

  If the push changes only `src/**`, also verify that `Build-Dotnet` runs. That also counts as the pending task 6.2 of the archived `restructure-source-layout`.
- [ ] 3.3 On the next push that changes only `tests/**` or only `helm/**`, verify that **no** `Check-Docker` run is triggered (spec scenario "Change outside the image does not trigger an image build").
- [ ] 3.4 For every `Check-Docker` run observed in 3.1 to 3.3, verify that no `Build-Helm` run follows it, and that no new image version appears for that commit (spec: the check publishes nothing).
- [ ] 3.5 On the next `v*` tag, verify:
  - **No `Check-Docker` run** is started by the tag.
  - **`Build-Docker` runs as before:** the `Test` gate; build, push and sign; the bot's `Chart.yaml` bump; then `Build-Helm` publishes and signs the chart.

  This also covers the pending task 6.5 of the archived `restructure-source-layout`.
- [ ] 3.6 Pull-request scenario ("Application change is image-build checked"): this repository takes no PRs, so record it as **not run**. Structural evidence: task 1.1 verifies that the `pull_request` trigger lists the same paths as `push`.
