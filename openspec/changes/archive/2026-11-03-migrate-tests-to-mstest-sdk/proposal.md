## Why

`ExampleNetTests` still uses the classic VSTest setup: `Microsoft.NET.Test.Sdk`, explicit MSTest adapter and framework packages, and `coverlet.collector`. Microsoft's current path for MSTest is the `MSTest.Sdk` project SDK running on Microsoft.Testing.Platform (MTP), which the .NET 10 `dotnet test` supports natively.

Exploration turned up two further problems:

- **The production image ships the test suite.** The Dockerfile publishes the whole solution into one folder. The published output was 53 MB, and 35 MB of it was test-only: `ExampleNetTests.dll`, MSTest, `testhost`, coverlet and the code-coverage tooling. The image is 258 MB with that payload and 200 MB without it.
- **Nothing runs the tests.** CI never builds or tests the code. A `v*` tag on a commit with failing tests still produces a signed image and a published chart.

## What Changes

- **Test project.** Move `ExampleNetTests` to `MSTest.Sdk/4.5.2` on MTP. This removes the explicit `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework` and `coverlet.collector` references; the SDK supplies MSTest 4.5.2, the same version used today. The test project keeps the SDK's default extensions (TRX report and Microsoft code coverage), adds the GitHub Actions reporter, and is marked non-publishable.
- **New root `global.json`.** It selects MTP for `dotnet test`. This is required: under the .NET 10 SDK, `dotnet test` on an `MSTest.Sdk` project fails without it. It also sets a .NET 10 SDK floor so that local, CI and container builds all use a 10.0 SDK.
- **BREAKING (developer workflow):** code coverage moves from `--collect "XPlat Code Coverage"` (coverlet) to `--coverage`. MTP rejects the old flag. The other test commands are unchanged: `dotnet test`, `--no-build`, `--filter`, and a bare solution or project path.
- **Container.** The Dockerfile restores and publishes only `ExampleNet/ExampleNet.csproj`, so the image no longer contains test files.
- **New `Build-Dotnet` workflow.** It builds and tests on every push and pull request to `main` that touches the application, the test project or the build configuration. It writes a test summary to the run page and uploads the TRX and coverage files.
- **Release gate.** `Build-Docker` runs `Build-Dotnet` first. Build, push, signing and the `Chart.yaml` bump happen only when it passes. `Build-Helm` publishes a chart from a `Build-Docker` run only when that run succeeded.
- Update `CLAUDE.md` (commands, CI/CD section, container build note).

There are no changes to the service's HTTP endpoints, port, configuration contract or Helm chart templates.

## Capabilities

### New Capabilities

- `ci-test-gate`: changes to the application or its tests are built and tested automatically on pushes and pull requests to `main`, and a release produces nothing unless its tests pass.

### Modified Capabilities

- `service-runtime`: adds a requirement that the container image contains only the application's runtime files, with no test assemblies, test frameworks, test runners or coverage tooling.

## Impact

- **Code and build configuration:**
  - `ExampleNetTests/ExampleNetTests.csproj`
  - new `global.json` at the repository root
- **Container:** `Dockerfile`. The image content changes: about 52 test-only files are removed, and the image shrinks from about 258 MB to about 200 MB (planning spike). The runtime payload of the application is unchanged.
- **CI:**
  - new `.github/workflows/build-dotnet.yaml`
  - `.github/workflows/build-docker.yaml` (test gate, plus the missing path in `helm lint`, which Helm 4 requires)
  - `.github/workflows/build-helm.yaml` (run only after a successful `Build-Docker`)

  Release runs take a few minutes longer because the tests run first.
- **Docs:** `CLAUDE.md`. The `README.md` and `.claude/agents/dotnet-developer.md` need no change; their commands still work.
- **Developer environment:**
  - a .NET 10 SDK is required, which `global.json` now enforces;
  - Visual Studio Test Explorer runs the tests through MTP (VS 2026 is installed locally).
- **Unchanged:** `ExampleNet` source and project file, `SettingsTests.cs`, the Helm chart templates, `docker-compose.yml`, `pod-example.yaml`.
