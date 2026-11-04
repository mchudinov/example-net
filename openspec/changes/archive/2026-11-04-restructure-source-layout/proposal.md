## Why

The repository root mixes the .NET solution and both projects with the deployment infrastructure: Dockerfile, Helm chart, workflows, OpenSpec and docs. The goal is the layout EuGo-docint uses:
- the solution and the application under `src/`;
- the tests under `tests/`;
- everything else at the root.

The move changes the paths the container build uses, so this change also fixes two weaknesses there:
- **The Docker build copies the whole repository.** There is no `.dockerignore`, and the Dockerfile runs `COPY . ./`. The build therefore receives the local `bin/`, `obj/`, `.vs/`, `.git`, `TestResults/`, `helm/` and `openspec/`. If the gitignored `appsettings.local.json` exists, it also ends up inside locally built images. `build-docker.sh` pushes such an image to GHCR.
- **Every source change reruns the NuGet restore.** The restore runs after the full copy, so it has no Docker layer of its own.

## What Changes

- **Move the projects:**
  - `ExampleNet/` to `src/ExampleNet/`;
  - `ExampleNetTests/` to `tests/ExampleNetTests/`;
  - `ExampleNet.slnx` to `src/ExampleNet.slnx`.

  Project names and namespaces stay the same. `global.json` stays at the root.
- **Update the references.** The solution's project paths change, and so does the test project's `ProjectReference`, which becomes `..\..\src\ExampleNet\ExampleNet.csproj`.
- **BREAKING (developer workflow): name the solution.** `dotnet restore`, `dotnet build` and `dotnet test` run at the root no longer find a solution; they fail with MSB1003. Use one of these instead:
  - `dotnet build src/ExampleNet.slnx`;
  - run the command from `src/`;
  - `dotnet run --project src/ExampleNet`.

  Visual Studio opens `src/ExampleNet.slnx`.
- **Update `Build-Dotnet`:**
  - its path filters become `src/**`, `tests/**`, `global.json` and the workflow file;
  - restore, build and test name the solution;
  - the test step writes to a fixed `TestResults/` folder at the workspace root, which the upload step already reads.
- **Change the container build:**
  - add a `.dockerignore`;
  - the Dockerfile copies `global.json` and the project file, restores, then copies the application source and publishes with `--no-restore`;
  - `.dockerignore` excludes `appsettings.local.json` by its exact name. `appsettings.localcontainer.json` stays in local images, because Docker Compose relies on it.
- **Update the docs:** `CLAUDE.md` and `README.md`.

The following stay the same:
- the HTTP endpoints, port and configuration contract;
- the Helm chart templates;
- `Build-Docker` and `Build-Helm`;
- the contents of released images, which is still only the application's publish output. Images built by CI never contained the gitignored local settings files.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. No path appears in the `ci-test-gate` or `service-runtime` requirements, and they hold unchanged after the move. The change therefore sets `skip_specs: true`.

## Impact

- **Moved:** every tracked file under `ExampleNet/` and `ExampleNetTests/`, and `ExampleNet.slnx`.
- **Edited:**
  - `src/ExampleNet.slnx`
  - `tests/ExampleNetTests/ExampleNetTests.csproj`
  - `Dockerfile`
  - `.github/workflows/build-dotnet.yaml`
  - `CLAUDE.md`
  - `README.md`
- **New:** `.dockerignore`.
- **Unchanged:**
  - `global.json`
  - `.github/workflows/build-docker.yaml`
  - `.github/workflows/build-helm.yaml`
  - `docker-compose.yml`
  - `build-docker.sh`
  - `helm/`
  - `pod-example.yaml`
  - `openspec/specs/`
- **Developer environment:**
  - After pulling, other clones still have the old `ExampleNet/` and `ExampleNetTests/` folders, holding only ignored files.
  - Local `appsettings.local*.json` files have to be moved to `src/ExampleNet/` by hand.
- **`dotnet-developer` agent:** its plain restore, build and test commands fail at the root. Briefs and `CLAUDE.md` have to give it the exact commands.
- **Follow-up:** change `ci-docker-build-check` will add a Docker build check on `main` and a layer cache for release builds.
