## 1. Test project and SDK selection (`dotnet-developer` agent: one brief, one commit, design D8)

- [x] 1.1 Rewrite `ExampleNetTests/ExampleNetTests.csproj` (design D1, D3, D4-B):
  - `<Project Sdk="MSTest.Sdk/4.5.2">`
  - keep `TargetFramework` (`net10.0`), `ImplicitUsings`, `Nullable` and the `ProjectReference` to `..\ExampleNet\ExampleNet.csproj`;
  - remove the `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework` and `coverlet.collector` references, and `IsPackable`;
  - add `<IsPublishable>false</IsPublishable>`;
  - add `<EnableMicrosoftTestingExtensionsGitHubActionsReport>true</EnableMicrosoftTestingExtensionsGitHubActionsReport>`.

  XML comments must not contain `--`, so don't write option names like `--report-gh` inside comments (MSB4025).

  Verify:
  - `dotnet restore ExampleNetTests/ExampleNetTests.csproj` succeeds.
  - `dotnet list ExampleNetTests/ExampleNetTests.csproj package` lists `MSTest.TestAdapter` 4.5.2, `MSTest.TestFramework` 4.5.2, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Testing.Extensions.TrxReport` and `Microsoft.Testing.Extensions.GitHubActionsReport`, and does not list `Microsoft.NET.Test.Sdk` or `coverlet.collector`.
- [x] 1.2 Add `global.json` at the repository root with exactly the content in design D2: an SDK floor of `10.0.100` with `rollForward: latestFeature`, and `test.runner` set to `Microsoft.Testing.Platform`. Verify that `dotnet --version` run from the repository root prints a `10.0.x` version.
- [x] 1.3 From the repository root, run `dotnet restore`, then `dotnet build --no-restore`, then `dotnet test --no-build`. Verify:
  - The build succeeds, and its only warning is the existing `CS8605` in `TimedHostedService.cs`.
  - The test output ends with an MTP `Test run summary: Passed!` showing `total: 1` and `succeeded: 1`.
- [x] 1.4 Run `dotnet test --no-build --filter "FullyQualifiedName~SettingsTests.SettingsTest"`, and verify 1 test runs and passes. Then run `dotnet test --no-build --filter "FullyQualifiedName~DoesNotExist"`, and verify it exits with code 8 (zero tests). This is the local check for the spec scenario "No tests discovered fails the check".
- [x] 1.5 Run `dotnet test --no-build --coverage --coverage-output-format cobertura --report-trx`. Verify:
  - The top level of `TestResults/` at the repository root contains one `*.trx` file and one `*.cobertura.xml` file. The TRX report also keeps an attachment copy of the cobertura file in a subfolder; that copy is expected.
  - `git status --porcelain` shows nothing under `TestResults/`, because it is gitignored.
  - `dotnet test --no-build --collect "XPlat Code Coverage"` fails, with a message pointing to `--coverage`.
- [x] 1.6 Run `dotnet list ExampleNet.sln package --vulnerable --include-transitive`. Verify it reports no vulnerable packages for `ExampleNet` or `ExampleNetTests` (spec: No known vulnerable dependencies).
- [x] 1.7 Run `dotnet publish -c Release -o <temp-dir>` for the whole solution, with the output directory outside the repository. Verify that `ls <temp-dir>` contains no `ExampleNetTests.*`, `MSTest*`, `testhost.*`, `Microsoft.Testing.*`, `Microsoft.TestPlatform.*` or `coverlet.*` entries and no `Microsoft.CodeCoverage` folder (design D4-B). Then delete the temp directory.
- [x] 1.8 The agent commits tasks 1.1 and 1.2 on its `agent/<slug>` branch. Verify that `git show --stat HEAD` lists only `ExampleNetTests/ExampleNetTests.csproj` and `global.json`.

## 2. Container image (main session, on the agent branch, design D4-A)

- [x] 2.1 In `Dockerfile`, change `RUN dotnet restore` to `RUN dotnet restore ExampleNet/ExampleNet.csproj`, and `RUN dotnet publish -c Release -o out` to `RUN dotnet publish ExampleNet/ExampleNet.csproj -c Release -o out`. Leave the images, `EXPOSE`, `ENV` and `ENTRYPOINT` unchanged.

  Verify:
  - `docker build -t example-net-docker .` succeeds.
  - Its output has no `NETSDK1194`.

  This needs a running Docker engine. If none is available, record 2.1 to 2.3 as **not run**, with the error, instead of checking them off.
- [x] 2.2 List the image's application directory:

  ```sh
  c=$(docker create example-net-docker)
  docker export "$c" | tar -t | grep -E '^App/' | grep -ciE 'test|mstest|coverlet|codecoverage|TestPlatform|Microsoft\.Testing'
  docker rm "$c"
  ```

  Verify the count is `0` (spec: Container image contains only the application). Also record the size from `docker image ls example-net-docker`; the spike measured about 200 MB, down from about 258 MB.
- [x] 2.3 Run `docker run --rm -d --name example-net-check --read-only --user 1000:1000 -p 50001:50001 example-net-docker`. Verify that `GET /`, `/healthz`, `/readyz` and `/livez` on port 50001 each return `200`, and that `GET /` returns `Ok`. This covers the existing specs on HTTP endpoints and port and on the restricted security context. Then run `docker rm -f example-net-check`.

## 3. CI workflows (main session, on the agent branch, design D5 to D7)

- [x] 3.1 Create `.github/workflows/build-dotnet.yaml` (`name: Build-Dotnet`) as described in design D5:
  - **Triggers:** `workflow_dispatch`, `workflow_call`, and `push` plus `pull_request` on `main` with the five `paths`.
  - **Permissions:** top-level `permissions: contents: read`.
  - **Steps:** checkout v7, then setup-dotnet v6 with `10.0.x`, then restore, then `build --no-restore -c Release`, then `test --no-build -c Release --report-trx --coverage --coverage-output-format cobertura --report-gh`.
  - **Upload:** upload-artifact v7 of `TestResults/`, with `if: ${{ !cancelled() }}`.

  Verify by reading the file that each of these is present, and that `paths` lists exactly `ExampleNet/**`, `ExampleNetTests/**`, `ExampleNet.sln`, `global.json` and `.github/workflows/build-dotnet.yaml`.
- [x] 3.2 In `.github/workflows/build-docker.yaml`:
  - Add a first job `Test` that runs `uses: ./.github/workflows/build-dotnet.yaml` with `permissions: contents: read`, and no `runs-on` or `steps`.
  - Add `needs: Test` to `Build-and-Push-Docker` (design D6).
  - In `Update-Chart-Version`, change `helm lint --strict` to `helm lint . --strict` (design D7, prerequisite).

  Verify:
  - `name: Build-Docker` is unchanged.
  - `Sign-Docker-Image` and `Update-Chart-Version` still need `Build-and-Push-Docker`, so they skip whenever `Test` fails (spec: Releases wait for passing tests).
  - `grep -n "helm lint" .github/workflows/build-docker.yaml` shows only `helm lint . --strict`.
- [x] 3.3 In `.github/workflows/build-helm.yaml`, add `if: github.event_name != 'workflow_run' || github.event.workflow_run.conclusion == 'success'` to `Check-Helm` only (design D7).

  Verify:
  - The triggers are unchanged.
  - `Build-and-Push-Helm` and `Sign-Helm-Chart` still depend on `Check-Helm` through `needs`.
- [x] 3.4 Lint the workflows by running `docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:latest` from the repository root. Verify:
  - It reports no findings in lines added or changed by 3.1 to 3.3.
  - Record any findings in lines this change didn't touch as pre-existing. Don't fix them here.

  If Docker is unavailable, record this as **not run**.

  *Result (actionlint 1.7.12):* no findings in `build-dotnet.yaml` or in any line added or changed in the other two workflows. Six pre-existing `SC2086` (info) shellcheck notes remain, in `build-docker.yaml` "Catch Docker image version" and `build-helm.yaml` "Extract chart info". The same six appear when linting `main`'s copies, where those blocks start at lines 47 and 44.
- [x] 3.5 Simulate the CI test job on Linux. In a scratch copy of the branch, without `bin/` and `obj/`, run the `Build-Dotnet` restore, build and test commands inside `mcr.microsoft.com/dotnet/sdk:10.0` with `GITHUB_ACTIONS=true` and `GITHUB_STEP_SUMMARY=<file>` set. Verify:
  - The test command exits 0.
  - The summary file contains a test-results table with `Total` 1 and `Passed` 1.
  - `TestResults/` contains a `*.trx` and a `*.cobertura.xml` file.

## 4. Documentation (main session, on the agent branch, design D9)

- [x] 4.1 Update `CLAUDE.md`:
  - **Commands:** note that `global.json` selects Microsoft.Testing.Platform for `dotnet test`, and add `dotnet test --coverage` for coverage.
  - **CI/CD and versioning:** describe `Build-Dotnet` and its triggers, the `Test` gate at the start of `Build-Docker`, and `Build-Helm` following only successful `Build-Docker` runs.
  - **Container note:** the Dockerfile restores and publishes only `ExampleNet/ExampleNet.csproj`, and the test project is not publishable.

  Verify:
  - `grep -n "Build-Dotnet" CLAUDE.md` finds the CI/CD section.
  - `grep -n -e "--coverage" CLAUDE.md` finds the command.
  - `grep -niE "coverlet|XPlat" CLAUDE.md` returns nothing.

  Once the user approves, commit tasks 2.1, 3.1 to 3.3 and 4.1 on the agent branch. Verify that `git log --oneline main..HEAD` shows the agent's commit followed by the main-session commit(s).

## 5. Developer environment (run by the user)

- [x] 5.1 In Visual Studio 2026, open `ExampleNet.sln` on `main` (the agent branch was fast-forwarded into `main` and deleted), then build. Verify that Test Explorer finds `SettingsTests.SettingsTest`, and that **Run All** passes it. If the user hasn't done this, record it as **not run**.

  *Observed (user, 2026-11-03):* Test Explorer finds `SettingsTests.SettingsTest` under the `global.json` MTP runner setting, and **Run All** passes it.

## 6. Live verification (run by the user after pushing; record each one as **not run** until it is observed)

- [x] 6.1 Push the agent branch and open a PR to `main`. Verify that `Build-Dotnet` runs on the PR and succeeds, that the run page shows the test summary, and that the `TestResults` artifact contains the TRX and cobertura files (spec scenario: Passing change).

  *Observed (PR #1, run 41492621127):*
  - The `Build-and-Test` check passed in 42 s, with all steps green.
  - The runner used SDK 10.0.403. CS8605 was the only warning, and the tests passed 1/1.
  - The `TestResults` artifact (6.8 KB) holds `ExampleNetTests_net10.0_x64.trx`, the `*.cobertura.xml` and the TRX attachment copy.
  - The job summary on the run page shows the test-results table (total 1, passed 1, failed 0). The user confirmed this on the run page on 2026-11-03, because neither the CLI nor the API exposes job summaries.
- [ ] 6.2 Branch off the PR branch, add a deliberately failing test, and open a draft PR to `main`. Verify:
  - `Build-Dotnet` fails.
  - The run summary names the failing test.
  - The `TestResults` artifact is still uploaded.

  This is the spec scenario "Failing test fails the check". Close the PR and delete the branch afterwards.

  **Not run** (user decision, 2026-11-03: work directly on `main`, no branches or PRs). A failing test can't be pushed to `main` without breaking it. Indirect evidence: the planning spike ran the same `dotnet test … --report-gh` command on Linux (design, spike #11). With a failing test, it exited 2 and the job summary named the test.
- [x] 6.3 On the next PR, or push to `main`, that changes only `helm/**` files, verify that no `Build-Dotnet` run is triggered (spec scenario: Unrelated change does not trigger a run). The `push` and `pull_request` triggers use the same `paths`. Until such a change happens, task 3.1's `paths` check covers this structurally.

  **Pending at archive** (2026-11-03): no push to `main` has changed only `helm/**` files yet, so the live check hasn't happened. The user chose to archive anyway. Structural evidence on `main`: both the `push` and `pull_request` triggers of `build-dotnet.yaml` list only the five `paths` from 3.1, and none of them matches `helm/**`.

  *Still pending after archive (2026-11-03):* the bot's `Chart.yaml` commits (`0d09ac4`, `55671a3`) change only `helm/Chart.yaml`, and no `Build-Dotnet` run followed them. They don't count as evidence, though. The bot pushes with `GITHUB_TOKEN`, and GitHub doesn't start workflows from such pushes: `Build-Helm`'s own `push: helm/**` trigger didn't fire for them either. A push of a `helm/**`-only change by a user is still needed.

  *Observed (2026-11-03, via change `helm-publish-only-on-release`):* the user pushed commit `2d3dc12` on its own (`0efab1b..2d3dc12`). It changes only `helm/build-helm.sh`. It started only `Build-Helm` run 41658988349 (event `push`) and **no** `Build-Dotnet` run. The pull-request half is still not observed, because this repository takes no PRs. It uses the same `paths` list.
- [x] 6.4 After merge, verify that `Build-Dotnet` runs on the push to `main` and succeeds.

  *Observed:* `main` was fast-forwarded to `d4f63c6` and pushed on 2026-11-03, and GitHub marked PR #1 as merged. `Build-Dotnet` run 41568738323 (event `push`) succeeded. It was the only workflow triggered by that commit.
- [x] 6.5 On the next `v*` tag from a green `main`, verify:
  - `Build-Docker` runs `Test` first and then builds, pushes and signs the image;
  - the bot commits the `Chart.yaml` bump;
  - `Build-Helm` publishes and signs the chart.

  This is the spec scenario "Passing tests release as before".

  *Observed after archive (2026-11-03):*
  - **`v1.0.0`** (the repository's first tag):
    - `Build-Docker` run 41637210931 succeeded. Its jobs ran in this order: `Test / Build-and-Test`, then `Build-and-Push-Docker`, then `Update-Chart-Version` and `Sign-Docker-Image`.
    - The bot committed the `Chart.yaml` bump to 1.0.0 (`0d09ac4`).
    - `Build-Helm` run 41637458630 started after `Build-Docker` succeeded, as designed. `Check-Helm` then failed for a reason outside this change: `AbsaOSS/k3d-action@v2` installs k3d v5.4.6 by default, and k3d's floating `install.sh` now also downloads the release's `checksums.txt` (k3d-io/k3d#1653), which v5.4.6 doesn't have. This returned HTTP 404 and curl exit 22.
    - Fixed in `15612c1` by pinning `k3d-version: v5.9.0`. The manual `Build-Helm` run 41639101061 then published and signed chart 1.0.0.
  - **`v1.0.1`**, the full automatic path:
    - `Build-Docker` run 41640417663 succeeded, and the bot committed `55671a3` (`Chart.yaml` 1.0.1).
    - `Build-Helm` run 41640709665 (event `workflow_run`) succeeded, publishing and signing the chart.
    - The user confirmed that both builds worked.
- [ ] 6.6 *Optional; only after merge.* Verify the failing-release path live:
  1. Start a manual run (`workflow_dispatch`) of `Build-Docker` on a throwaway branch that contains a test which **certainly fails**.
  2. Verify that `Test` fails and that `Build-and-Push-Docker`, `Sign-Docker-Image` and `Update-Chart-Version` are skipped.
  3. Verify that the `Build-Helm` run it triggers shows `Check-Helm` as skipped.
  4. Delete the branch afterwards.

  This is the spec scenario "Failing tests block the release". **Warning:** if the tests pass on that branch, the run pushes an image tagged with the branch name and commits a `Chart.yaml` bump to `main`. If you skip this task, tasks 3.2 to 3.4 cover the scenario structurally.

  **Not run** (user decision, 2026-11-03: no branches or PRs). It needs a throwaway branch. Tasks 3.2 to 3.4 cover the scenario structurally.
