## Why

Released images only run on amd64: the v1.2.1 release built a single platform, so a pod scheduled on an arm64 node can't use them. The app itself has no architecture dependency, and both base images already include `linux/arm64`, so the gap is only in how the image is built.

The archived `ci-docker-build-check` design listed "Multi-arch builds" as a non-goal. This change reverses that.

## What Changes

- **Dockerfile:** the build stage runs natively on the build machine and cross-compiles for the target architecture (`--platform=$BUILDPLATFORM`, `ARG TARGETARCH`, `-a $TARGETARCH` on both `dotnet restore` and `dotnet publish`). The runtime stage doesn't change.
- **`Build-Docker`:** builds and pushes `linux/amd64` and `linux/arm64` under the existing version tags. A release still produces one digest, and the existing cosign steps sign and verify it unchanged.
- **`Check-Docker`:** builds the same two platforms, so a change that breaks either build fails on push or pull request instead of at the next release.
- **Build only.** No workflow starts the container, on either platform. The arm64 image's first real start is its first deployment. This continues the decision in the archived `ci-docker-build-check` change (D2).
- **The amd64 image's contents change slightly.** Publishing with `-a` makes `ExampleNet.dll` and `ExampleNet.deps.json` specific to the target architecture and drops `web.config`, an IIS file the container doesn't use.
- **Docs:** `CLAUDE.md` and the README describe the two platforms and the Dockerfile's build pattern.

**Out of scope:**
- 32-bit `linux/arm/v7`.
- Running the image in CI, or a release gate that runs it.
- Rebuilding existing tags. Version 1.2.1 and earlier stay amd64-only.
- `cosign sign --recursive`.
- `image.repository` in `helm/values.yaml`. Its default, `ghcr.io/wss-ea-cloud-devops/example-net-docker`, isn't where this repo's CI pushes (`ghcr.io/mchudinov/example-net-docker`), so default chart installs won't get the new image until it's overridden or fixed in a separate change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `service-runtime`: adds a requirement that each release publishes the container image for `linux/amd64` and `linux/arm64` under one signed reference. The existing running-container requirements don't change, and they apply to both platforms.
- `ci-test-gate`: the image build check must build every platform a release publishes.

## Impact

- **Build:** `Dockerfile`.
- **CI:** `.github/workflows/build-docker.yaml` and `.github/workflows/check-docker.yaml`.
- **Docs:** `CLAUDE.md` and `README.md`.
- **Unchanged:** the application code, `ExampleNet.csproj`, the tests, the Helm chart, `docker-compose.yml`, `Build-Dotnet` and `Build-Helm`. With no C# or project file changes, all the work stays in the main session.
- **Local builds:** `docker build` and `docker compose up` keep working, because BuildKit sets `BUILDPLATFORM` and `TARGETARCH` itself. The Dockerfile now needs BuildKit, which is already the default.
- **Cost:** each image build runs one more restore and publish (about 15 s locally, per `restructure-source-layout`) and pulls the arm64 runtime base image. This applies to both `Check-Docker` and releases.
- **Dependencies:** restore downloads `Microsoft.NETCore.App.Host.linux-arm64` from NuGet for the arm64 build. It's the arm64 apphost pack, and the build already needs NuGet access for its other packages.
