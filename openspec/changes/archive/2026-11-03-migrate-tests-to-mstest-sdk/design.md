## Context

See proposal.md (Why) for the motivation. This is the current state that shapes the approach:

- **Test project.** `ExampleNetTests` uses `Microsoft.NET.Sdk` with these packages:
  - `Microsoft.NET.Test.Sdk` 18.11.0, which runs tests on VSTest;
  - `MSTest.TestAdapter` and `MSTest.TestFramework` 4.5.2;
  - `coverlet.collector` 10.1.0.

  It holds one test, `SettingsTests.SettingsTest`. The repo has no `global.json`; the local SDK is 10.0.402.
- **Container build.** The Dockerfile runs `COPY . ./`, then `dotnet restore` and `dotnet publish -c Release -o out` against the whole solution. Both projects land in `out/`, and the SDK warns NETSDK1194 about it. There is no `.dockerignore`.
- **Workflows.**
  - `Build-Docker` runs on `v*` tags and manual dispatch. It builds, pushes and signs the image, and its bot commits the `Chart.yaml` version bump to `main`.
  - `Build-Helm` runs on `workflow_run` of `Build-Docker` with `types: [completed]`, whatever the conclusion. It also runs on pushes and PRs that touch `helm/**`.
  - No workflow runs `dotnet build` or `dotnet test`.
- **Work split** (CLAUDE.md):
  - `*.csproj` files and `ExampleNetTests/` go through the `dotnet-developer` agent. The agent must run `dotnet restore`, then `dotnet build --no-restore`, then `dotnet test --no-build` before every commit.
  - Dockerfile, workflow and documentation changes stay in the main session.

### Spike evidence

All checks below ran during planning on a scratch copy of HEAD, with `MSTest.Sdk/4.5.2`, the `global.json` from D2 and the container fix from D4 applied.

| # | Check | Result |
|---|---|---|
| 0 | `dotnet test` with no `global.json` | Fails: *"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later…"* |
| 1 | `dotnet restore`, then `build --no-restore`, then `test --no-build` | Only warning is the existing CS8605. 1/1 passed |
| 2 | `dotnet test --no-build --filter "FullyQualifiedName~SettingsTests.SettingsTest"` | 1/1 passed; the CLAUDE.md syntax works unchanged |
| 3 | A filter that matches nothing | Exit code 8 ("zero tests") |
| 4 | `dotnet test ExampleNet.sln`, `dotnet test <csproj>` and `--solution ExampleNet.sln` | All work on SDK 10.0.402 |
| 5 | `--coverage --coverage-output-format cobertura --report-trx` | Results written to `./TestResults/`, which is already gitignored. `--collect "XPlat Code Coverage"` is rejected (exit 5), with a pointer to `--coverage` |
| 6 | `dotnet list package --vulnerable --include-transitive` | Clean for both projects. The test project resolves MSTest.TestAdapter/TestFramework 4.5.2, Microsoft.Testing.Extensions.CodeCoverage 18.12.0 and TrxReport 2.5.2 |
| 7 | App-only publish | 55 files, 14 MB, no test files. HEAD's solution publish: 107 files, 53 MB. Every file only in HEAD's output came from test dependencies, including a NuGet copy of `Microsoft.Extensions.DependencyInjection.dll` |
| 8 | Solution-level publish with `IsPublishable=false` | 55 files, no test files. NETSDK1194 still warns |
| 9 | Docker image | 258 MB → 200 MB. Test-related paths under `/App`: 244 → 0 |
| 10 | Container run with `--read-only --user 1000:1000` | `/`, `/healthz`, `/readyz` and `/livez` return 200; `/` returns `Ok` |
| 11 | CI commands on Linux in `sdk:10.0` (SDK 10.0.403) with `--report-gh` | A passing run exits 0 and writes the job summary, including coverage. A failing test exits 2, and the summary names the failed test |
| 12 | GitHub `::error` annotations | **Not** emitted through `dotnet test`, even with `--output detailed`. Emitted, with file and line, when the test app is run directly |
| 13 | `global.json` SDK floor `10.0.100` with `latestFeature` | Resolves to 10.0.402 locally and 10.0.403 in `sdk:10.0`. App-only restore works. `dotnet test` still passes |

One authoring trap turned up along the way: an XML comment that contains `--` (for example `--report-gh`) makes the project file unloadable (MSB4025).

## Goals / Non-Goals

**Goals:**
- `dotnet test` stays the single way to run tests. Today's commands keep working, except coverage.
- No new build warnings. CS8605 remains the only one.
- Satisfy the new `service-runtime` requirement, the existing `service-runtime` requirements and both `ci-test-gate` requirements (see specs).

**Non-Goals:**
- Inline GitHub annotations on PR diffs (see D3).
- Making `Build-Dotnet` a required status check, or other branch-protection changes.
- Bumping action versions in the existing workflows.
- Building the Docker image in PR CI. Dockerfile changes are verified locally and at release time.
- Adding tests. The earlier non-goals also stay non-goals: `.slnx`, central package management, CS8605, and the README `docker run` port example.

## Decisions

Each decision is marked as either *agreed* (decided with the user during exploration) or *planning default* (proposed, open to change at review).

### D1. `MSTest.Sdk/4.5.2` on Microsoft.Testing.Platform (agreed)

The test project becomes:
- `<Project Sdk="MSTest.Sdk/4.5.2">`, keeping `TargetFramework`, `ImplicitUsings`, `Nullable` and the `ProjectReference`;
- without the four `PackageReference` items;
- without `IsPackable`, which the SDK already sets to false.

The SDK's default runner mode builds the tests as an MTP executable.

- **SDK and MSTest versions match.** `MSTest.Sdk` releases together with MSTest, so 4.5.2 supplies the same MSTest 4.5.2 used today.
- **The version goes in the `Sdk` attribute** (planning default). It sits next to the project it affects, and `global.json` keeps only toolchain choices. The alternative is the `msbuild-sdks` section of `global.json`.
- **Alternative rejected: `<UseVSTest>true</UseVSTest>`.** It needs no `global.json`, but it keeps `Microsoft.NET.Test.Sdk` and VSTest, which removes the point of the move.

### D2. `global.json`: MTP runner plus a .NET 10 SDK floor (runner required; floor is a planning default)

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

- **Runner.** Mandatory: without it, `dotnet test` fails (spike #0).
- **SDK floor.** Without a floor, each machine uses its newest installed SDK. GitHub-hosted runners will carry the .NET 11 SDK soon after its release, so CI could test with a different SDK than the one the `sdk:10.0` image builds with. With `latestFeature`, any installed 10.0 SDK at or above 10.0.100 qualifies and the highest one is used (spike #13); an 11.x SDK is never picked.
- **Alternatives:**
  - *Runner only* (what exploration first proposed): no SDK coupling, but local, CI and image builds can drift apart.
  - *An exact version pin*: rejected, because the floating `sdk:10.0` image would break whenever its SDK differs from the pin.
- **Coupling.** The floor changes together with the target framework and the Dockerfile's images, under CLAUDE.md's "Target framework" rule.
- **Inside the image build.** `COPY . ./` brings `global.json` into the build stage. The `sdk:10.0` image satisfies the floor, and the `test` section has nothing to act on there.

### D3. Test extensions and CI reporting (summary-only reporting agreed; extension set is a planning default)

- Keep the SDK's `Default` extension profile: TRX report and Microsoft code coverage.
- Add `<EnableMicrosoftTestingExtensionsGitHubActionsReport>true</EnableMicrosoftTestingExtensionsGitHubActionsReport>`. This is version 2.5.2, the same release line as the MTP packages. It only acts when `--report-gh` is passed.
- Remove `coverlet.collector`. It is a VSTest data collector and has no effect on MTP. Coverage moves from `--collect "XPlat Code Coverage"` to `--coverage --coverage-output-format cobertura`.
- **CI reporting.** CI runs `dotnet test … --report-gh`. The job summary (totals, failures, coverage, slowest tests) works through `dotnet test`; annotations don't (spike #12). This is accepted so that `dotnet test` stays the single entry point.
- **Alternatives:**
  - Extension profile `None`: leaner, but loses the TRX and coverage files that CI uploads.
  - coverlet's MTP integration: a third-party tool that duplicates the bundled coverage.
  - Running the test DLL directly to get annotations: needs one step per test project and bypasses `dotnet test`.

### D4. The image contains only the application: option C (agreed)

- **A, the fix (Dockerfile).** Use `RUN dotnet restore ExampleNet/ExampleNet.csproj` and `RUN dotnet publish ExampleNet/ExampleNet.csproj -c Release -o out`.
  - The image build never loads the test project, so test-only dependencies can't break or slow it.
  - This also removes NETSDK1194.
- **B, the backstop (test project).** `<IsPublishable>false</IsPublishable>`, so any solution-level publish skips the test project (spike #8). That covers a local publish, a future CI step or another Dockerfile.
- **Alternatives:**
  - A only: relies on every future publish path targeting the app project.
  - B only: keeps the solution-wide `-o` publish, its warning, and the test project in the image build.
  - A `.dockerignore` excluding `ExampleNetTests/`: a third mechanism that isn't needed once A is in place.
- **Effect.** About 52 test-only files leave the image. The application's runtime payload is unchanged (spike #7).

### D5. The `Build-Dotnet` workflow (triggers and summary-only agreed; other details are planning defaults)

- **File and name.** `.github/workflows/build-dotnet.yaml`, `name: Build-Dotnet`.
- **Triggers:**
  - `workflow_dispatch`;
  - `workflow_call`;
  - `push` and `pull_request` on `main`, with `paths`: `ExampleNet/**`, `ExampleNetTests/**`, `ExampleNet.sln`, `global.json`, `.github/workflows/build-dotnet.yaml`.

  Path filters don't apply to `workflow_call`, so the workflow always runs when called. That is intended.
- **Permissions.** Top-level `permissions: contents: read`.
- **Job** (one, on `ubuntu-latest`):
  1. checkout
  2. `actions/setup-dotnet` with `dotnet-version: 10.0.x`
  3. `dotnet restore`
  4. `dotnet build --no-restore -c Release`
  5. `dotnet test --no-build -c Release --report-trx --coverage --coverage-output-format cobertura --report-gh`
  6. upload `TestResults/` as an artifact, with `if: ${{ !cancelled() }}` so that failed runs keep their result files

  Steps 3 to 5 mirror the `dotnet-developer` sequence. A run that discovers zero tests exits 8 and fails the job (spike #3).
- **Action versions.** Use the current majors: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`. At planning time the latest releases were 7.0.1, 6.0.0 and 7.0.2. The existing workflows stay on `checkout@v6` (non-goal).
- **Dockerfile not in `paths`.** This workflow doesn't exercise the Dockerfile.

### D6. Release gate in `Build-Docker` (agreed)

- Add a first job:

  ```yaml
  Test:
    uses: ./.github/workflows/build-dotnet.yaml
    permissions:
      contents: read
  ```

  A job that calls a reusable workflow cannot have `runs-on` or `steps`.
- `Build-and-Push-Docker` gets `needs: Test`. `Sign-Docker-Image` and `Update-Chart-Version` already need `Build-and-Push-Docker`, so they skip too when tests fail.
- `./…` resolves to the workflow file at the caller's commit, so the tagged source is the source tested.
- Keep the workflow name `Build-Docker`, because `Build-Helm` refers to it by name.
- **Trade-off.** Tests run again at tag time, although the push to `main` already ran them. That costs a minute or two. Reusing the earlier result would need lookups across runs.

### D7. `Build-Helm` follows only successful `Build-Docker` runs (planning default)

- Add `if: github.event_name != 'workflow_run' || github.event.workflow_run.conclusion == 'success'` to `Check-Helm`. `Build-and-Push-Helm` and `Sign-Helm-Chart` then skip through `needs`.
- **Why.** `types: [completed]` also fires on failed runs. Today a failed `Build-Docker` still re-publishes and re-signs the current chart version. With the gate, failed releases become routine. This condition is what makes the spec's "no chart published or signed as a follow-on of that release run" true.
- **Unchanged.** The push, PR and dispatch triggers.
- **Prerequisite: fix `helm lint` in `Build-Docker`'s `Update-Chart-Version` job.** The job runs `helm lint --strict` without a path.
  - Helm 4 requires the path. Local Helm 4.1.4 fails with *"helm lint" requires at least 1 argument*, and the job uses the runner's preinstalled Helm, whose version isn't pinned.
  - The bot's last `Chart.yaml` bump was 0.2.54 on 2026-04-14, so this step passed then. It will fail once the runners carry Helm 4.
  - Today such a failure still lets `Build-Helm` publish the stale chart version. Under D7 it would stop chart publishing entirely.
  - So change it to `helm lint . --strict`, the form CLAUDE.md documents.
- **Alternative.** Leave `Build-Helm` as it is, and remove the chart clause from the "Releases wait for passing tests" requirement and its scenario. The lint fix is still worth making, but it is no longer a prerequisite.
- **Note.** `workflow_run` workflows run from the default branch's copy, so this condition only takes effect after merge.

### D8. Work split (planning default, following CLAUDE.md)

- **`dotnet-developer` brief:** `ExampleNetTests/ExampleNetTests.csproj` and `global.json`, together. The agent's mandatory `dotnet test --no-build` fails without `global.json` (spike #0). The agent creates its `agent/<slug>` branch and commits there.
- **Main session, on the same branch afterwards:** the Dockerfile, the three workflows and `CLAUDE.md`.
- **Alternative rejected:** putting the Dockerfile in the brief so everything lands in one commit. CLAUDE.md assigns Dockerfile changes to the main session, and the agent's build and test gate doesn't exercise the Dockerfile.

### D9. Documentation (planning default)

- **`CLAUDE.md`:**
  - **Commands:** `global.json` selects MTP for `dotnet test`; coverage is `dotnet test --coverage`.
  - **CI/CD:** the new `Build-Dotnet` workflow, the test gate in `Build-Docker`, and `Build-Helm` following only successful `Build-Docker` runs.
  - **Container:** the Dockerfile builds only `ExampleNet.csproj`, and the test project is not publishable.
- **Unchanged:** `.claude/agents/dotnet-developer.md`, because its commands still work (spike #4), and `README.md`, which has no unit-test instructions.

## Risks / Trade-offs

- **[Risk]** Visual Studio Test Explorer under the `global.json` MTP setting wasn't exercised during planning. → **Mitigation:** a manual check by the user in VS 2026 (18.11.1, installed).
- **[Risk]** CI behaviour can't be observed before a push, and the agent never pushes. → **Mitigation:**
  - a local actionlint run;
  - a local Linux run of the CI commands, as in spike #11;
  - live checks marked **not run** until the user pushes or opens a PR.
- **[Risk]** The `Build-Helm` condition only takes effect from the default branch, so the failing-release scenario can't be observed before merge. → **Mitigation:** structural verification (the `needs` chain and the `if`) plus actionlint. A live check is optional and needs care (see tasks).
- **[Risk]** Under D7, a failure in **any** `Build-Docker` job stops chart publishing for that release, not just a test failure. That includes signing through the floating `sigstore/cosign-installer@main`, the bump commit and the lint. → **Mitigation:** the `helm lint . --strict` fix (D7). Such a failure shows up as a failed `Build-Docker` run, and re-running the release recovers it.
- **[Risk]** If `Build-Dotnet` later becomes a required status check, its path filters will block PRs that don't touch .NET files. → **Mitigation:** none now (non-goal). If it is made required, add a job that always runs and skips its own work when nothing relevant changed.
- **[Trade-off]** No inline annotations (spike #12). The job summary and the TRX artifact carry the failure details.
- **[Trade-off]** Tests run twice per release (D6).
- **[Risk]** The SDK floor rejects machines that only have a newer SDK installed, such as 11.x only. → **Mitigation:** install a 10.0 SDK. The floor moves together with the target framework.
- **[Risk]** A developer runs `--collect "XPlat Code Coverage"` from habit. → **Mitigation:** MTP's error message points to `--coverage`, and CLAUDE.md documents it.
- **[Risk]** The workflows use different `actions/checkout` majors (v6 and v7). → **Mitigation:** cosmetic only. Bump the old ones in a separate change.

## Migration Plan

1. The agent commits the project file and `global.json`. The main session then commits the Dockerfile, workflows and `CLAUDE.md` on the same `agent/<slug>` branch.
2. The user pushes the branch and opens a PR to `main`. `Build-Dotnet` runs on the PR from the PR's own workflow file. This is the first live check of the first `ci-test-gate` requirement.
3. Merge. `Build-Dotnet` runs on the push to `main`.
4. On the next `v*` tag:
   - `Build-Docker` runs `Test` first, then builds, pushes and signs the image, and the bot bumps `Chart.yaml`;
   - `Build-Helm` then publishes and signs the chart, as before.

   The image now has no test files and is about 58 MB smaller. Mention that in the release notes.
5. **Rollback.** Revert the commits.
   - Revert the project file and `global.json` **together**. An `MSTest.Sdk` project without `global.json` fails `dotnet test` (spike #0), and a VSTest project under the MTP runner setting is not supported either.
   - The workflow and Dockerfile changes can be reverted on their own.
   - Earlier image tags and chart versions are unaffected.

## Open Questions

- Should `Build-Dotnet` become a required status check on `main`? This can be decided later without changing this change's specs or tasks (see Risks).
