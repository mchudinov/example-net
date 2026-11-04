## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach:

- **Layout.** The root holds:
  - `ExampleNet.slnx`;
  - `ExampleNet/`;
  - `ExampleNetTests/`, whose `ProjectReference` is `..\ExampleNet\ExampleNet.csproj`;
  - `global.json`, which sets an SDK floor of `10.0.100` with `latestFeature` and selects Microsoft.Testing.Platform (MTP) as the `dotnet test` runner;
  - `Dockerfile`, `docker-compose.yml`, `build-docker.sh`, `helm/`, `.github/` and `openspec/`.
- **Container build.**
  - The Dockerfile runs `COPY . ./`, then restores and publishes `ExampleNet/ExampleNet.csproj`. There is no `.dockerignore`.
  - `ExampleNet.csproj` marks `appsettings.local.json` and `appsettings.localcontainer.json` as `CopyToOutputDirectory=Always`. When either file exists locally, a locally built image contains it.
  - Images built by CI never contain them, because both files are gitignored.
- **`Build-Dotnet`:**
  - path filters: `ExampleNet/**`, `ExampleNetTests/**`, `ExampleNet.slnx`, `global.json` and the workflow file;
  - plain `restore`, `build` and `test` commands at the root;
  - uploads `TestResults/` with `upload-artifact`. When it finds no files, that action only warns by default.
- **`Build-Docker`** runs on `v*` tags and uses `context: ./`. This change leaves it as it is.
- **Work split.**
  - Per `CLAUDE.md`, `*.cs`, `*.csproj`, `*.slnx`, `appsettings*.json` and the test project go through the `dotnet-developer` agent. Dockerfile, workflow and docs changes stay in the main session.
  - Per the user's standing instruction, all commits go to `main`, with no branches or PRs.
- **Reference: EuGo-docint.**
  - Its solution is `src/DocInt.slnx`, with project paths `../tests/...` and `../tools/...`.
  - `Directory.Build.props` and `aspire.config.json` sit at its root.
  - Its Dockerfile copies the project files, restores, copies the source, then publishes with `--no-restore`. It has a `.dockerignore`.
  - Every CI command names `src/DocInt.slnx`.
  - Its root `Directory.Build.props` is never copied into its Docker build. The image therefore compiles without the nullable-as-errors setting that CI uses.

### Planning evidence

All probes ran during exploration on SDK 10.0.402 in MTP mode, against the current layout. They were read-only: `--list-tests` with `--no-build`, and no reports.

| # | Check | Result |
|---|---|---|
| 1 | `dotnet test --help` | Usage is `dotnet test [options]`. It offers `--project` and `--solution` |
| 2 | From `helm/`, which has no solution: `dotnet test ../ExampleNet.slnx --no-build --list-tests` | 1 test discovered. A positional path to an existing solution works. Spike #4 of the archived `migrate-tests-to-mstest-sdk` change found the same |
| 3 | From `helm/`: `dotnet test --solution ../ExampleNet.slnx --no-build --list-tests` | Same result |
| 4 | From `helm/`: `dotnet test ../Typo.slnx --no-build --list-tests` | *"Specify a project or solution file. The current working directory does not contain a project or solution file."* |
| 5 | From `helm/`: `dotnet test --solution ../Typo.slnx ...` | *"The provided file path does not exist: …\Typo.slnx."* |
| 6 | From the root, where a solution exists: `dotnet test NoSuch.slnx --no-build` | Runs the root solution and passes `NoSuch.slnx` to the test app: *"Tool 'NoSuch.slnx' not found"*, exit 5 |
| 7 | From `helm/`: `dotnet build --no-restore` | MSB1003 |
| 8 | From `helm/`: `dotnet test --no-build --list-tests ../ExampleNet.slnx` and `dotnet test --no-build ../ExampleNet.slnx --list-tests` | 1 test discovered in both. The solution path is picked up wherever it appears among the options |

These are expectations only, not yet observed. Each has a verify task:
- **(E1)** Without `**/obj` in `.dockerignore`, copying the source after the restore overwrites the container's `obj/project.assets.json` with the host's copy. `publish --no-restore` then fails, typically with NETSDK1064.
- **(E2)** Without a fixed `--results-directory`, it is unknown where `TestResults/` lands once the solution sits in `src/`.
- **(E3)** `git mv` of a folder renames the folder on disk, so ignored files inside it (`bin/`, `obj/`, local settings) move with it in the clone where the move is done.

## Goals / Non-Goals

**Goals:**
- The root contains no .NET project or solution. `global.json` is the only .NET file left there.
- Every command in `CLAUDE.md`, `README.md` and CI works after the move.
- `Build-Dotnet` still runs on every change to the application, the tests or the build configuration they share (`ci-test-gate`).
- The image built from a `v*` tag contains the same files as before.
- Local Docker builds no longer receive `bin/`, `obj/` or other repository noise, and no longer put `appsettings.local.json` into the image.

**Non-Goals:**
- A Docker build check on `main` and a layer cache in CI. Both belong to the follow-up change `ci-docker-build-check`.
- Renaming projects, assemblies or namespaces.
- Adding `Directory.Build.props` or central package management.
- Copying EuGo-docint's multi-arch build or its `USER $APP_UID`.
- Changing `docker-compose.yml` or the README's container examples. This includes the `docker run --publish 50001:8080` port example, and the localcontainer example that binds Kestrel to `localhost`.
- Editing the shared `.claude/agents/dotnet-developer.md` (see D10).
- Branch protection or required status checks.

## Decisions

Each decision is marked *agreed* (decided with the user during exploration) or *planning default* (proposed, open to change at review).

### D1. Projects in `src/ExampleNet` and `tests/ExampleNetTests` (agreed)

The tests sit in their own `tests/` folder, as in EuGo-docint, rather than under `src/`.

The test project's reference becomes `..\..\src\ExampleNet\ExampleNet.csproj`. Every other moved file is a pure rename. The test project file changes by one line, so git still detects it as a rename.

**Alternative: both projects under `src/`** (the original request). The `ProjectReference` would stay unchanged, CI would need one path filter fewer, and a future `Directory.Build.props` could live in `src/`. It was rejected in favour of matching EuGo-docint's `src/` + `tests/` split.

### D2. Solution at `src/ExampleNet.slnx` (agreed)

```xml
<Solution>
  <Project Path="ExampleNet/ExampleNet.csproj" />
  <Project Path="../tests/ExampleNetTests/ExampleNetTests.csproj" />
</Solution>
```

Consequence: plain `dotnet` commands at the root fail with MSB1003 (probe 7). The proposal marks this BREAKING for the developer workflow.

**Alternative: keep the solution at the root.** All plain commands would keep working. It was rejected because the goal is a root without .NET projects or solutions.

### D3. `global.json` stays at the root (planning default)

There is no real choice here:
- The `dotnet` host looks for `global.json` in the current folder and then in each parent folder.
- If the file were in `src/`, commands run from the root would never see it. That covers CI, and the Docker build, which runs in `/App`.
- Those commands would then lose the MTP opt-in, and `dotnet test` fails for an `MSTest.Sdk` project without it.
- They would also lose the SDK floor.

Commands run from `src/` or `tests/` still find the file in the parent folder. EuGo-docint has no `global.json` at all, which isn't an option here.

### D4. Commands name the solution with a positional path (planning default)

The commands match EuGo-docint and the agent's "pass the solution explicitly" rule:

```sh
dotnet restore src/ExampleNet.slnx
dotnet build --no-restore src/ExampleNet.slnx
dotnet test  --no-build  src/ExampleNet.slnx
```

Locally, `cd src` followed by plain commands also works.

Alternatives considered:
- **`--solution src/ExampleNet.slnx` for `dotnet test`.** Its error names a missing path (probe 5), where the positional form gives only a generic error (probe 4). It was not chosen because, at the root, both forms fail clearly once no solution remains there, and positional keeps all three commands the same shape.
- **`defaults.run.working-directory: src` in CI.** The commands could stay plain. It was rejected because it would move `TestResults/` away from where the upload step reads it (see D5).

### D5. Fixed results directory in CI (planning default)

The test step adds `--results-directory ${{ github.workspace }}/TestResults`, and the upload `path: TestResults/` stays unchanged.

The default location after the move is unknown (E2). `upload-artifact` only warns when it finds no files, so a wrong location would lose the results while the run stays green. Fixing the directory removes that question.

### D6. CI path filters (planning default)

Both `push` and `pull_request` use the same list:
- `src/**`, which also covers `src/ExampleNet.slnx`;
- `tests/**`;
- `global.json`;
- `.github/workflows/build-dotnet.yaml`.

Changes under `helm/**` still don't trigger the workflow (`ci-test-gate`: "Unrelated change does not trigger a run").

### D7. Docker build stage: restore in its own layer (agreed in principle; details are planning defaults)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /App

# Restore layer: rebuilt only when global.json or the project file changes
COPY global.json ./
COPY src/ExampleNet/ExampleNet.csproj src/ExampleNet/
RUN dotnet restore src/ExampleNet/ExampleNet.csproj

# Source layer
COPY src/ExampleNet/ src/ExampleNet/
RUN dotnet publish src/ExampleNet/ExampleNet.csproj -c Release --no-restore -o out
```

The runtime stage stays unchanged: same images, `EXPOSE`, `ENV`, `COPY --from` and `ENTRYPOINT`.

- **Repository paths are kept inside the image**, rather than flattened as EuGo-docint does. A root-level build file can then be copied to the matching path inside the image.
- **`global.json` is copied.** The container then builds under the same SDK floor as CI. If someone raises `global.json` without also changing the `sdk:10.0` image, the build fails with an error instead of quietly using the older SDK. `CLAUDE.md` already says the two change together.
- **Rule: every root-level file that affects the build needs its own `COPY`.** Today that is only `global.json`. In future it would include `Directory.Build.props`, `Directory.Packages.props` and `NuGet.config`. EuGo-docint shows what happens otherwise: its `Directory.Build.props` never reaches its Docker build. This rule goes into `CLAUDE.md`.

### D8. `.dockerignore` (agreed: the file itself and the exact-name exclusion; the rest is a planning default)

```
**/bin
**/obj
**/.vs
.git
TestResults
tests
helm
openspec
**/appsettings.local.json
```

- **D7 depends on `**/bin` and `**/obj`.** Without them, the source copy overwrites the container's restore output (E1). That makes the D7 layering worse than today's Dockerfile.
- **`appsettings.local.json` is excluded by its exact name.** Copying the `.gitignore` pattern `appsettings.local*.json` would also exclude `appsettings.localcontainer.json`, which Docker Compose relies on (`DOTNET_ENVIRONMENT=localcontainer`). `appsettings.local.json` serves `dotnet run` through the `local` launch profile, and an image never needs it.
- **`tests` keeps the test project out of the build entirely.** This is a second safeguard for the `service-runtime` requirement "Container image contains only the application", in addition to the Dockerfile publishing only `ExampleNet`.

### D9. Two commits on `main`, split as `CLAUDE.md` assigns them (planning default)

This follows the archived `migrate-tests-to-mstest-sdk` change:
1. **The agent's commit:** the moves, `src/ExampleNet.slnx` and the test project file.
2. **The main session's commit:** `Dockerfile`, `.dockerignore`, `build-dotnet.yaml`, `CLAUDE.md` and `README.md`.

Both land on `main` and are pushed together after the user approves. CI only runs on the pushed head commit.

The trade-off: the agent's commit on its own has a Dockerfile and CI filters that still point at the old paths. That only matters if someone later uses bisect.

**Alternative: one agent commit that includes the Dockerfile, workflow and docs.** It extends the `CLAUDE.md` exception for chart changes by analogy, and gives one self-contained commit. It was not chosen because `CLAUDE.md` explicitly keeps those files in the main session, and its exception names only chart changes.

### D10. The agent gets exact commands; the shared agent file stays unchanged (planning default)

The agent's built-in loop runs plain `dotnet restore`, `dotnet build --no-restore` and `dotnet test --no-build` from the root. After the move, these fail with MSB1003. Its rule 2, "identify the right solution", only applies when there are several solution files.

So the brief for task 1 and the Commands section in `CLAUDE.md` both give the D4 commands. The brief also says to commit on `main` and not to create a branch.

`.claude/agents/dotnet-developer.md` is identical in this repo and EuGo-docint, and it stays unchanged. A rule-2 case for "one solution, not at the root" is noted as a low-priority edit to both copies later.

### D11. Documentation (planning default)

**`CLAUDE.md`:**
- the delegation paths: `src/ExampleNet/appsettings*.json` and `tests/ExampleNetTests/`;
- the solution paragraph: it lives at `src/ExampleNet.slnx`, every command names it, and it must stay the only solution file;
- the Commands section: the D4 commands, with `src/` paths for build output and `dotnet run`;
- the Container build note: the D7 layering, the D8 `.dockerignore`, and the rule about root-level build files;
- the Configuration flow: what Docker builds do with each local settings file;
- the CI path list;
- a note that `global.json`, and any future `Directory.Build.props`, stays at the root.

**`README.md`:**
- the build command;
- the output path, `src/ExampleNet/bin/<Debug|Release>/net10.0/`;
- the Visual Studio solution path.

## Risks / Trade-offs

- **[Build-Dotnet quietly stops running on source changes if the filters are wrong]** → D6. Check the filters by reading the file, then live on the next push that changes only `src/`, and on one that changes only `tests/`. The push of the move itself can't serve as the check, because it changes the workflow file and so triggers a run whatever the filters say.
- **[Test results not uploaded, with only a warning]** → D5. Verified locally and on the first CI run.
- **[The source copy overwrites the restore output in the Docker build]** → D8 excludes `bin` and `obj`. Verified by building with a local `bin/` and `obj/` present.
- **[A root-level build file is missed by the targeted copy]** → the D7 rule, written into `CLAUDE.md`.
- **[Plain `dotnet` commands at the root fail]** → marked BREAKING in the proposal; the docs give the D4 commands; `cd src` works.
- **[Other clones keep leftover folders, and local settings stay behind]** → see the Migration Plan.
- **[On Windows, the folder rename fails if a file is locked]** → before moving, close Visual Studio and any VS Code window that has the repository open, then run `dotnet build-server shutdown`. During apply, a VS Code window's C# Dev Kit `BuildHost` blocked the first `git mv` with `Permission denied`.
- **[Locally built images no longer contain `appsettings.local.json`]** → intended. `appsettings.localcontainer.json` is still included.

## Migration Plan

1. Close Visual Studio and any VS Code window that has the repository open (C# Dev Kit holds the project folders), then run `dotnet build-server shutdown`.
2. Land the two D9 commits on `main`. Ask before pushing.
3. In every other clone, after pulling:
   - move `ExampleNet/appsettings.local*.json` to `src/ExampleNet/`;
   - delete the leftover `ExampleNet/` and `ExampleNetTests/` folders, which now hold only ignored files;
   - don't use `git clean -X` for this, because it also deletes the local settings files;
   - in Visual Studio, open `src/ExampleNet.slnx`. The old `.vs/` state can be deleted.

**Rollback:** revert both commits. Nothing that has been deployed changes, and the image contents are unchanged.
