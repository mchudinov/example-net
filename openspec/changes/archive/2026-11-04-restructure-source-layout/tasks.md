## 1. Move the projects and the solution (`dotnet-developer` agent: one brief, one commit on `main`, design D9–D10)

The brief must say:
- Commit on `main`. Do not create a branch, push or open a PR.
- Use exactly these commands from the repository root, in this order:

  ```sh
  dotnet restore src/ExampleNet.slnx
  dotnet build --no-restore src/ExampleNet.slnx
  dotnet test --no-build src/ExampleNet.slnx
  ```

- [x] 1.1 *Main session and user, before sending the brief.*
  1. **Record the warning baseline with a full rebuild:** from the root, on the current layout, run `dotnet build --no-incremental`.
     - The archived `migrate-tests-to-mstest-sdk` change recorded the existing CS8605 in `TimedHostedService.cs` as the only warning.
     - A warm incremental build can report zero warnings because it recompiles nothing, so don't use one as the baseline.
     - Put the baseline list into the brief as existing warnings that the agent must not fix.
  2. **Release locked files:** the user closes Visual Studio and any VS Code window that has the repository open, then run `dotnet build-server shutdown`. C# Dev Kit's project system holds the project folders open. If this session runs in VS Code's terminal, closing that window ends the session, so resume it from a plain terminal. On Windows, a locked file makes the folder rename fail.

  Verify that `git status --porcelain` is empty.

  *Result (2026-11-04):*
  - **Baseline:** `dotnet build --no-incremental` gives 0 errors and 1 warning, `TimedHostedService.cs(92,44): warning CS8605`. This matches the archived record.
  - **Locked files:** Visual Studio was not running, and `dotnet build-server shutdown` stopped the MSBuild and compiler servers.
  - **`git status --porcelain`:** shows only the untracked planning folders `openspec/changes/restructure-source-layout/` and `openspec/changes/ci-docker-build-check/`. There are no tracked changes.
- [x] 1.2 Create `src/` and `tests/`, then run:

  ```sh
  git mv ExampleNet src/ExampleNet
  git mv ExampleNetTests tests/ExampleNetTests
  git mv ExampleNet.slnx src/ExampleNet.slnx
  ```

  Verify:
  - `git status` shows only renames.
  - The root has no `ExampleNet/`, no `ExampleNetTests/` and no `*.slnx`.

  Record whether the ignored contents (`bin/`, `obj/`, any `appsettings.local*.json`) moved along. This is expectation E3. If they stayed behind in a leftover `ExampleNet/` folder, move any `appsettings.local*.json` to `src/ExampleNet/` by hand, then delete the leftover folder.
- [x] 1.3 In `src/ExampleNet.slnx`, set the two project paths from design D2. Verify that `dotnet sln src/ExampleNet.slnx list` shows `ExampleNet/ExampleNet.csproj` and `../tests/ExampleNetTests/ExampleNetTests.csproj`, or the same paths with backslashes.
- [x] 1.4 In `tests/ExampleNetTests/ExampleNetTests.csproj`, change only the `ProjectReference`, to `..\..\src\ExampleNet\ExampleNet.csproj` (design D1). Verify that `git diff -M --stat` shows that file as a rename with one changed line.
- [x] 1.5 From the repository root, run the three brief commands. Then, for the warning comparison, also run `dotnet build --no-restore --no-incremental src/ExampleNet.slnx`. The moved `obj/` folders may let the normal build skip compiling, so only a full rebuild reliably shows the warnings. Verify:
  - The build succeeds.
  - The `--no-incremental` build's warnings match the 1.1 baseline exactly. A baseline warning is not a new warning, so leave it alone.
  - The test output ends with `Test run summary: Passed!`, with `total: 1` and `succeeded: 1`.
- [x] 1.6 Check the other ways of running commands (design D2, D3, D4):
  - Plain `dotnet build --no-restore` at the root fails with MSB1003.
  - `cd src && dotnet test --no-build` passes 1/1, so `global.json` is found from `src/`.
  - From the root, `dotnet run --project src/ExampleNet` starts the app, and `GET http://localhost:50001/healthz` returns 200. Then stop the app.
- [x] 1.7 History survives the move. Verify:
  - `git log --follow --oneline src/ExampleNet/Program.cs` lists commits from before the move.
  - After the commit, `git show -M --stat HEAD` shows the `.cs` files as renames.
- [x] 1.8 The agent commits tasks 1.2 to 1.4 on `main`. Verify:
  - `git branch --show-current` prints `main`.
  - `git show --stat HEAD` lists only the moved files, `src/ExampleNet.slnx` and `tests/ExampleNetTests/ExampleNetTests.csproj`.

  *Result for 1.2 to 1.8 (2026-11-04, commit `d146b5c` on `main`, not pushed):*
  - **First attempt:** `git mv ExampleNet src/ExampleNet` failed with `Permission denied`. A VS Code window with C# Dev Kit held the project folders; its `BuildHost` process is a Dev Kit helper. Nothing changed. The user closed that window and resumed from Windows Terminal. A rename-and-restore test then succeeded for both folders, and the retry went through.
  - **E3 (observed):** `git mv` moved the ignored contents with the folders: `bin/`, `obj/` and the empty `tests/ExampleNetTests/TestResults/`. No leftover folder remained at the root. No `appsettings.local*.json` existed.
  - **Solution:** `dotnet sln src/ExampleNet.slnx list` shows `..\tests\ExampleNetTests\ExampleNetTests.csproj` and `ExampleNet\ExampleNet.csproj`.
  - **Build:** restore succeeded. Both the normal build and an extra `dotnet build --no-restore --no-incremental src/ExampleNet.slnx` gave 0 errors and 1 warning, the baseline `src\ExampleNet\TimedHostedService.cs(92,44): warning CS8605`.
  - **Tests:** `Test run summary: Passed!`, total 1, succeeded 1.
  - **1.6:** bare `dotnet build --no-restore` at the root fails with MSB1003. `cd src && dotnet test --no-build` passes 1/1. With `dotnet run --project src/ExampleNet`, `/healthz` returns 200. No process was left running afterwards.
  - **History:** `git log --follow` for `src/ExampleNet/Program.cs` lists 21 commits from before the move.
  - **Renames:** `git show --name-status HEAD` shows `R100` for 9 files, `R088` for the test `.csproj` (one changed line) and `R051` for the `.slnx` (one changed path line). Both edited files keep CRLF line endings.

## 2. Container build (main session, design D7–D8)

These tasks need a running Docker engine. If none is available, record 2.3 to 2.7 as **not run**, with the error.

- [x] 2.1 Create `.dockerignore` with exactly the content in design D8. Verify that `grep -n "appsettings" .dockerignore` shows only `**/appsettings.local.json`.
- [x] 2.2 Rewrite the Dockerfile's build stage as in design D7, and leave the runtime stage unchanged. Verify that `git diff Dockerfile` touches only the build stage.
- [x] 2.3 Keep the local `bin/` and `obj/` that task 1.5 left in `src/ExampleNet/`, then run `docker build -t example-net-docker .`. Verify that it succeeds. This shows that D8 keeps the host's `obj/` out.

  *Optional:* temporarily remove `**/obj` from `.dockerignore`, rebuild, record the error to confirm expectation E1, then restore the line.

  *Result (2026-11-04, Docker 29.9.2, SDK 10.0.403 in the image):*
  - **Build:** succeeded with the host's `src/ExampleNet/bin/` and `obj/` present.
  - **Build context:** 14.50 kB, so `bin/`, `obj/`, `.git`, `helm/` and the rest were not sent.
  - **Warnings:** only the baseline CS8605.
  - **Optional E1 check:** done. With `**/obj` removed, the build failed (exit 1) at `RUN dotnet publish … --no-restore`. The error was not the NETSDK1064 the design expected but this:

    ```
    error MSB4018: The "ResolvePackageAssets" task failed unexpectedly. …
    NuGet.Packaging.Core.PackagingException: Unable to find fallback package folder
    'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages'
    ```

    The host's Windows `project.assets.json` had overwritten the container's copy. Afterwards `.dockerignore` was restored, byte-identical, and the throwaway image was removed.
- [x] 2.4 Restore caching works. Append a blank line to `src/ExampleNet/Program.cs` and run `docker build -t example-net-docker .` again. Verify:
  - The `RUN dotnet restore` step shows `CACHED`.
  - The `RUN dotnet publish` step runs again.

  Then run `git checkout -- src/ExampleNet/Program.cs`.

  *Result:* `COPY global.json`, `COPY …/ExampleNet.csproj` and `RUN dotnet restore` all showed `CACHED`. `COPY src/ExampleNet/` and `RUN dotnet publish` ran again, the publish taking 5.3 s. `Program.cs` was restored and is clean.
- [x] 2.5 Check what the image contains.
  1. If either of `src/ExampleNet/appsettings.local.json` or `src/ExampleNet/appsettings.localcontainer.json` is missing, create it with the content `{}`. Never overwrite an existing one.
  2. Rebuild, then run:

     ```sh
     c=$(docker create example-net-docker)
     docker export "$c" | tar -t | grep -E '^App/' > /tmp/app-files.txt
     docker rm "$c"
     ```

  Verify:
  - `App/appsettings.localcontainer.json` is present.
  - `App/appsettings.local.json` is absent.
  - `grep -ciE 'test|mstest|coverlet|codecoverage|TestPlatform|Microsoft\.Testing' /tmp/app-files.txt` prints `0` (spec: Container image contains only the application).

  Delete only the dummy files you created, then rebuild without them before 2.6.

  *Result:*
  - **Dummy files:** neither real file existed, so both dummies were created and deleted afterwards.
  - **Settings files in the image:** `App/appsettings.json` and `App/appsettings.localcontainer.json` are present. `App/appsettings.local.json` is absent.
  - **Test payload:** the count is `0`.
  - **Extra check, against the design goal "the image built from a `v*` tag contains the same files as before":**
    - I rebuilt from the tree without the dummies, and built `24aaf8d` (the old Dockerfile and layout) from a clean scratch `git worktree`.
    - Their sorted `/App` listings are identical, 22 entries each. The image is 183 MB.
    - The worktree and the old image were removed afterwards.
- [x] 2.6 Run `docker run --rm -d --name example-net-check --read-only --user 1000:1000 -p 50001:50001 example-net-docker`. Verify:
  - `GET /`, `/healthz`, `/readyz` and `/livez` on port 50001 each return 200.
  - `GET /` returns `Ok`.

  Then run `docker rm -f example-net-check`. This covers the `service-runtime` endpoint and security-context requirements.

  *Result:* `/`, `/healthz`, `/readyz` and `/livez` each returned `200`, and `/` returned `Ok`. The container is removed.

  One quirk of this machine: `curl http://localhost:…` resolves to `::1` first, and Docker Desktop's IPv6 forward never answers (curl exit 28). The checks therefore used `http://127.0.0.1:50001` with `--max-time`. This is unrelated to this change.
- [x] 2.7 With no `src/ExampleNet/appsettings.localcontainer.json` present, run `docker compose up --build -d`. Verify that `GET http://localhost:8080/healthz` returns 200, then run `docker compose down`.

  *Result:* `docker compose up --build -d` built the image and started it. `GET http://127.0.0.1:8080/healthz` returned `200` on the first try (127.0.0.1 because of the `::1` quirk noted in 2.6). `docker compose down` removed the container and the network.

## 3. CI (main session, design D4–D6)

- [x] 3.1 In `.github/workflows/build-dotnet.yaml`:
  - Set the `paths` of both `push` and `pull_request` to exactly `src/**`, `tests/**`, `global.json` and `.github/workflows/build-dotnet.yaml`.
  - Change Restore to `dotnet restore src/ExampleNet.slnx`.
  - Change Build to `dotnet build --no-restore -c ${{ env.CONFIGURATION }} src/ExampleNet.slnx`.
  - Change Test to exactly this:

    ```
    dotnet test --no-build -c ${{ env.CONFIGURATION }} src/ExampleNet.slnx
    --results-directory ${{ github.workspace }}/TestResults
    --report-trx
    --coverage --coverage-output-format cobertura
    --report-gh
    ```

    Keep it in the existing `>-` folded block.
  - Leave the upload `path: TestResults/` unchanged.

  Verify by reading the file that both triggers list the same four paths, and that `grep -nE "'ExampleNet(Tests)?/\*\*'|'ExampleNet\.slnx'" .github/workflows/build-dotnet.yaml` returns nothing.

  *Result:*
  - **Paths:** `push` and `pull_request` both list `src/**`, `tests/**`, `global.json` and `.github/workflows/build-dotnet.yaml`. A comment notes that `src/**` includes the solution.
  - **Old paths:** the grep returns nothing (exit 1).
  - **Commands:** Restore, Build and Test name `src/ExampleNet.slnx`, as specified. Test adds `--results-directory ${{ github.workspace }}/TestResults`, with a one-line comment.
  - **Upload:** the step is unchanged. The file keeps CRLF line endings.
- [x] 3.2 Run the CI commands locally from the root.
  1. Run `dotnet build --no-restore -c Release src/ExampleNet.slnx`.
  2. Run the Test command from 3.1 word for word, with three substitutions:
     - `Release` for `${{ env.CONFIGURATION }}`;
     - `$PWD` for `${{ github.workspace }}`;
     - drop `--report-gh`, unless `GITHUB_ACTIONS=true` and `GITHUB_STEP_SUMMARY` are set.

  Verify:
  - The test step exits 0.
  - The top level of `TestResults/` at the root has one `*.trx` and one `*.cobertura.xml`.

  Also record, for information, where the results land when `--results-directory` is left out. This settles expectation E2.

  *Result:*
  - **Old results:** the existing `TestResults/` from earlier runs was moved to the session scratchpad first, so the check started clean.
  - **Build:** `dotnet build --no-restore -c Release src/ExampleNet.slnx` succeeded, with only the baseline CS8605.
  - **Test:** the literal Test command, with `$PWD` and without `--report-gh`, exited 0 with `Test run summary: Passed!` (total 1, succeeded 1).
  - **Files:** `TestResults/` at the root has exactly `33824de1-….cobertura.xml` and `ExampleNetTests_net10.0_x64.trx`.
  - **E2 (observed):** without `--results-directory`, run from the root, the `.trx` still lands in the root's `TestResults/`, which is relative to the current folder. Fixing the folder therefore wasn't strictly needed for CI, which runs from the workspace root. It keeps the upload correct if the default changes or the command is run from another folder.
- [x] 3.3 From the root, run `docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:latest`. Verify that no finding points at a line this change added or edited. If Docker is unavailable, record this as **not run**.

  *Result:* in Git Bash, the command was run as `MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/repo" -w /repo rhysd/actionlint:latest` (actionlint 1.7.12).
  - `build-dotnet.yaml` has **0 findings**.
  - The only findings are the six pre-existing `SC2086` (info) shellcheck notes: three in `build-docker.yaml:54` and three in `build-helm.yaml:46`. The archived `migrate-tests-to-mstest-sdk` change recorded the same six. This change doesn't touch either file.
- [x] 3.4 Verify that `git diff --stat` shows no change to `build-docker.yaml`, `build-helm.yaml`, `docker-compose.yml`, `build-docker.sh`, `global.json` or `helm/`.

  *Result:* `git diff --stat 24aaf8d -- <those paths>` is empty, which covers both the agent's commit and the working tree. `git status` shows only `build-dotnet.yaml` and `Dockerfile` modified, the new `.dockerignore`, and the two untracked change folders.

## 4. Documentation (main session, design D11)

- [x] 4.1 Update `CLAUDE.md` as listed in design D11. Verify:
  - Every `ExampleNet/` and `ExampleNetTests/` path in it starts with `src/` or `tests/`. Check this by reading the output of `grep -n "ExampleNet" CLAUDE.md`.
  - The Commands section uses the D4 commands.
  - The Container build note mentions `.dockerignore` and the rule about root-level build files.

  *Result:* sections updated, as listed in D11:
  - **Delegation paths.**
  - **New "Layout" block:** the solution in `src/` and `MSB1003` at the root; `MSB1011` within `src/`; `dotnet sln src/ExampleNet.slnx add`; `global.json` and any future `Directory.Build.props` at the root.
  - **Commands:** the agent's three-command loop, then the other commands with `src/` paths.
  - **Test runner:** the `tests/` path for the test project.
  - **Container build:** the restore layer, the rule about root-level build files, and `.dockerignore`, including the observed MSB4018 failure from 2.3.
  - **Configuration flow:** where the local settings files live, and what Docker builds do with each one.
  - **CI path list:** plus the fixed results directory.

  Checks:
  - `grep -n "ExampleNet" CLAUDE.md`: every path starts with `src/` or `tests/`. The only other mentions are the project name (line 7) and the file name `ExampleNet.csproj` (line 72). CRLF line endings are kept.
  - **New commands:** run to confirm. `dotnet test src/ExampleNet.slnx --no-build --filter "FullyQualifiedName~SettingsTests.SettingsTest"` passes 1/1, and a filter that matches nothing exits 8.
- [x] 4.2 Update `README.md`: the build command, the output path `src/ExampleNet/bin/<Debug|Release>/net10.0/` and the Visual Studio solution path. Verify that `grep -n "ExampleNet/bin" README.md` shows only the `src/` path.

  *Result:*
  - **Specified edits:** the Visual Studio section opens `src/ExampleNet.slnx`. The command line runs `dotnet build src/ExampleNet.slnx --configuration Release` from the repository root. The output path is `<repository root>/src/ExampleNet/bin/<Debug|Release>/net10.0/`, and `grep` finds only that one.
  - **Beyond the D11 list (needed because of the move):**
    - The Docker image and Docker Compose sections said to run "from the solution's directory" or "from solution's root directory". That is now `src/`, where both commands would fail. They now say "from the repository root".
    - The local settings examples now name `src/ExampleNet/` as the location, and what Docker builds do with each file.
  - **Left as is:** the `docker run --publish 50001:8080` example (a non-goal).
- [x] 4.3 Once the user approves, commit tasks 2.1, 2.2, 3.1, 4.1 and 4.2 as one main-session commit on `main`. Verify that `git log --oneline -2` shows the agent's commit (task 1.8) followed by this commit.

  **Ask the user before pushing.**

  *Result (2026-11-04):*
  - **Approval:** the user approved the commit and the push. At the user's choice, the commit also includes this change's folder, `openspec/changes/restructure-source-layout/`. `ci-docker-build-check/` stays untracked.
  - **Commit:** `0459911` on `main`, after the agent's `d146b5c`, as `git log --oneline -2` shows.
  - **Push:** `origin/main` was still `24aaf8d` before the push. The first `git push` was rejected by GitHub with `Internal Server Error`; the retry pushed `24aaf8d..0459911`.
  - **Not in the commit:** the edits to this file from 4.3 and 6.1 were made after the commit.

## 5. Developer environment (run by the user)

- [x] 5.1 In Visual Studio 2026, open `src/ExampleNet.slnx` and build. Verify:
  - Solution Explorer shows both projects.
  - Test Explorer finds `SettingsTests.SettingsTest`, and **Run All** passes it.

  If the user hasn't done this, record it as **not run**.

  *Observed (user, 2026-11-04):* the user opened `src/ExampleNet.slnx` in Visual Studio 2026 and reported: "done all good".

## 6. Live verification (run by the user after pushing; record each check as **pending** until it is observed)

- [x] 6.1 On the push of the two commits, verify:
  - `Build-Dotnet` runs and succeeds.
  - The `TestResults` artifact contains the `*.trx` and `*.cobertura.xml`. That proves D5 works on the runner.

  This run doesn't test the new filters: the push changes the workflow file itself and renames every project file, so the workflow would run with any filters.

  *Observed (2026-11-04):* `Build-Dotnet` run 41813112059 (event `push`, `0459911`) succeeded, with every step green. It was the only workflow the push triggered.
  - **Commands:** the run used `dotnet restore src/ExampleNet.slnx` and `dotnet build --no-restore -c Release src/ExampleNet.slnx`. The test step ran `dotnet test --no-build -c Release src/ExampleNet.slnx --results-directory /home/runner/work/example-net/example-net/TestResults --report-trx --coverage --coverage-output-format cobertura --report-gh`.
  - **Warnings:** only the baseline CS8605 (`src/ExampleNet/TimedHostedService.cs(92,44)`).
  - **Tests:** `Test run summary: Passed!`, total 1, succeeded 1.
  - **Artifact:** `TestResults` (6775 bytes) holds `ExampleNetTests_net10.0_x64.trx`, `c9f7f482-….cobertura.xml`, and the TRX attachment copy of the cobertura in a subfolder. That is the same shape as the archived change's 6.1.
- [ ] 6.2 Verify that the next push that changes only files under `src/` triggers `Build-Dotnet`.

  **Pending at archive** (2026-11-04): no push since the move has changed only `src/`. The user chose to archive anyway. Structural evidence: both triggers list `src/**` (task 3.1). A Build-Dotnet run on the move push doesn't count, because that push changed the workflow file (6.1).
- [ ] 6.3 Verify that the next push that changes only files under `tests/` triggers `Build-Dotnet`.

  **Pending at archive** (2026-11-04): no push since the move has changed only `tests/`. Structural evidence: both triggers list `tests/**` (task 3.1).
- [ ] 6.4 Verify that the next push by a user that changes only `helm/**` does **not** trigger `Build-Dotnet` (`ci-test-gate` scenario: Unrelated change does not trigger a run). The bot's `Chart.yaml` commits don't count, because pushes made with `GITHUB_TOKEN` start no workflows.

  **Pending at archive** (2026-11-04): no user push has changed only `helm/**` since the move. Related live evidence: the user's push `401d706` changed only files under `openspec/` and started **no** workflow: `gh run list --commit "$(git rev-parse 401d706)"` is empty. Corrected 2026-11-04 during `ci-docker-build-check`: this note first cited `gh run list --commit 401d706`, which is invalid evidence, because `--commit` silently matches nothing for a short SHA. Re-checked with the full SHA, the result is still no runs. That is an unrelated change in the sense of the `ci-test-gate` scenario, but not the `helm/**` case this task names.
- [ ] 6.5 On the next `v*` tag, verify:
  - `Build-Docker`'s `Test` gate passes.
  - The image builds with the new Dockerfile, and is pushed and signed.
  - The bot commits the `Chart.yaml` bump.
  - `Build-Helm` publishes and signs the chart.

  Until the follow-up `ci-docker-build-check` lands, this is the first time CI builds the new Dockerfile.

  **Pending at archive** (2026-11-04): no `v*` tag has been pushed since the move. Local evidence: the new Dockerfile builds (2.3), and its `/App` listing is identical to the old layout's, 22 entries each (2.5). The image runs read-only as 1000:1000 (2.6).
