## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach:

- **Dockerfile.** The build stage has no platform setting and runs `dotnet restore` and `dotnet publish` with no runtime identifier. The runtime stage has no `RUN` instruction: only `FROM`, `WORKDIR`, `EXPOSE`, `ENV`, `COPY` and `ENTRYPOINT ["dotnet", "ExampleNet.dll"]`.
- **Base images.** `sdk:10.0` and `aspnet:10.0-noble-chiseled` both list `linux/arm64` (`docker buildx imagetools inspect`, 2026-11-05).
- **Application.** There is no P/Invoke in the C# source. The publish output has no `runtimes/` folder and no native libraries, and `ExampleNet.deps.json` has no `runtimeTargets`.
- **Release build (v1.2.1 log, run 42004779668).**
  - Docker 29.4.2 with the `docker` driver and the containerd image store (`driver-type: io.containerd.snapshotter.v1`).
  - The builder lists only `linux/amd64` variants, so arm64 emulation isn't available on it.
  - The push is an OCI index holding one platform manifest and one attestation manifest.
  - `cosign sign` targets the index digest: `ec48d81b…` is the digest on the "exporting manifest list" line.
- **Limit of this evidence.** The GHCR package is private and the local `gh` token lacks `read:packages`, so the manifest itself wasn't read. The single-platform finding comes from the log and the absence of a `platforms` input.

**Publish spike** (scratch copy of the source, SDK 10.0.402 on Windows, `--os linux` added because the host isn't Linux):

| Command | `ExampleNet.dll` machine | Apphost | `deps.json` target |
|---|---|---|---|
| No `-a` | AnyCPU (`0x014c`) | The host's (`.exe` here) | `.NETCoreApp,Version=v10.0` |
| `restore` and `publish` with `-a amd64` | x64 (`0x8664`) | ELF x86-64 | `linux-x64` |
| `restore` and `publish` with `-a arm64` | arm64 (`0xaa64`) | ELF aarch64 | `linux-arm64` |
| `restore` without `-a`, `publish --no-restore -a arm64` | Fails with `NETSDK1047` | | |

The `-a amd64` and `-a arm64` outputs have identical file lists. Both remain framework-dependent, with no bundled runtime. Compared with the no-`-a` output, they don't contain `web.config`.

No container image was built or run during planning, because the local Docker daemon wasn't running.

## Goals / Non-Goals

**Goals:**
- One release produces one digest that serves both architectures, so tags, signing and the chart's image reference keep their current shape.
- A change that breaks the image build on either platform fails `Check-Docker`.
- Nothing executes as arm64 during the build, so no emulation is needed.

**Non-Goals:**
- **Emulation or ARM runners.** Neither QEMU nor `ubuntu-24.04-arm` is added.
- **Running tests on arm64.** `Build-Dotnet` stays on `ubuntu-latest`.
- **A layer cache.** The extra build time doesn't justify one (see `ci-docker-build-check` D3).

## Decisions

Each decision is marked *agreed* (decided with the user during exploration) or *planning default* (proposed, open to change at review).

### D1. Platforms are `linux/amd64` and `linux/arm64` (agreed)

The user's only ARM target is `linux/arm64`. The base images also offer `linux/arm/v7`, but it would add a third build that nobody needs.

### D2. Build only, no container run (agreed)

`Check-Docker` and `Build-Docker` build both platforms and don't start the container. This continues `ci-docker-build-check` D2.

Alternatives the user rejected:
- **Smoke test in `Check-Docker`.** A job on `ubuntu-24.04-arm`, optionally also on amd64, that runs the image read-only as 1000:1000 and probes `/healthz`.
- **The same plus a release gate.** Pull the pushed digest on an ARM runner and probe it before signing.

Either stays a possible later change. The tasks check each image's architecture without starting it, by exporting its filesystem.

This limits verification, not the requirement. The running-container requirements in `service-runtime` apply to both platforms, but CI doesn't check them on either one, and arm64 is first proven at its first deployment. Task 6.2 records that result.

### D3. Cross-compile in the build stage (planning default)

```dockerfile
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
ARG TARGETARCH
...
RUN dotnet restore src/ExampleNet/ExampleNet.csproj -a $TARGETARCH
...
RUN dotnet publish src/ExampleNet/ExampleNet.csproj -c Release --no-restore -a $TARGETARCH -o out
```

The SDK always runs natively on the build machine. The runtime stage only copies files, so BuildKit executes nothing for the arm64 image.

Alternatives rejected:
- **QEMU emulation of the build stage.** .NET doesn't support running under QEMU, and it would be slower.
- **A native-runner matrix** (`ubuntu-latest` plus `ubuntu-24.04-arm`) that pushes by digest and merges with `docker buildx imagetools create`. It adds jobs and a merge step and changes the digest `Sign-Docker-Image` receives. It only pays off if the build has to execute arm64 code, which it doesn't.

### D4. `-a $TARGETARCH` rather than one portable publish (planning default)

This matches [Microsoft's chiseled sample](https://github.com/dotnet/dotnet-docker/blob/main/samples/AspNetCoreRazorApp/Dockerfile.chiseled). This repo is a reference service, so following that pattern also makes it a better one to copy. The pattern stays correct if the app later gains native packages, ReadyToRun or a self-contained publish.

- **`-a` goes on both commands.** Without it on restore, `publish --no-restore` fails with `NETSDK1047`, so a mismatch fails loudly.
- **`TARGETARCH=amd64` is accepted.** BuildKit passes `amd64`, not `x64`, and the SDK maps it (spike).
- **`ENTRYPOINT` stays `dotnet ExampleNet.dll`.** The sample starts the apphost instead, but nothing here needs that change.

Alternative rejected: **one portable publish with `-p:UseAppHost=false`**, with no `-a`.
- **For:** the build runs once, both images get the same AnyCPU DLL, and the amd64 payload changes less.
- **Against:** it departs from the documented pattern, needs revisiting for ReadyToRun or self-contained, and ships every architecture's native assets once a native package appears. Without `UseAppHost=false`, the arm64 image would carry an x64 apphost.

**Consequence for amd64:** `ExampleNet.dll` becomes x64-specific, `deps.json` targets `linux-x64`, and `web.config` is dropped. See Risks.

### D5. The same `platforms` in both workflows, on the default builder (planning default)

Add `platforms: linux/amd64,linux/arm64` to the build step in `build-docker.yaml` and `check-docker.yaml`, with a comment in each saying the two lists must match. `CLAUDE.md` already says `Check-Docker` builds the way a release does.

Keep the default builder. The containerd image store supports multi-platform builds on it, and `ci-docker-build-check` D3 deliberately left out `docker/setup-buildx-action`. `Check-Docker` run 42087897329 confirmed this on a runner.

**Fallback:** if a runner image change ever makes the default builder reject multi-platform, add `docker/setup-buildx-action` to both workflows in one commit, and update the "default builder" wording in `CLAUDE.md` and the `check-docker.yaml` comment. The failure would be loud, and it would happen in a check rather than in a release. Don't add `docker/setup-qemu-action` in either case.

### D6. Signing stays as it is (planning default)

`Sign-Docker-Image` already signs the index digest from `build-push-action`'s `digest` output, and multi-platform builds keep that shape. The index pins each platform manifest by digest, and `cosign verify` on a tag checks the index digest.

Alternative rejected for now: **`cosign sign --recursive`**, which also signs each platform manifest. It's only needed if something verifies per-platform digests, and it would add behaviour to a path that can only be exercised by a real release. It's a one-flag follow-up.

### D7. Documentation (planning default)

- **`CLAUDE.md`:**
  - the Container build note: the build stage's cross-compile pattern, and that the runtime stage must stay free of `RUN`, because a `RUN` there would execute as arm64;
  - the Target framework bullet: a new runtime image must include `linux/arm64`;
  - the CI/CD entries for `Check-Docker` and `Build-Docker`: both build the two platforms, and their lists must match.
- **`README.md`:** the GitHub actions section says images are published for both platforms, starting with the first release after 1.2.1.

The README's existing errors (the `docker run --publish 50001:8080` example and the `WSS-EA-Cloud-DevOps` wording) are left alone.

## Risks / Trade-offs

- **[The arm64 image doesn't start, for a reason a build can't see]** → Accepted (D2). Examples are a future package with no `linux-arm64` native asset, or a base image problem. It would first show at deployment. The build-only architecture checks reduce the risk but don't remove it.
- **[The amd64 image's contents change and no task starts it]** → This is the main cost of combining D2 and D4. The tasks compare the old and new `/App` listings and check the architecture of each file without running the image. Earlier changes also ran `docker run --read-only --user 1000:1000` by hand; this change doesn't, under the user's "no smoke tests" decision.
- **[The default builder rejects multi-platform]** → D5's fallback.
- **[The multi-platform push and signing first run on a real tag]** → `Check-Docker` covers the build, but not the push or the signing. If either fails, `Build-Docker` fails, `Build-Helm` doesn't publish, and the recovery is the next patch tag, as it is today.
- **[The two `platforms` lists drift apart]** → Comments in both workflows, the `CLAUDE.md` note and the `ci-test-gate` requirement.
- **[A later `RUN` in the runtime stage needs emulation]** → On the runner it would fail the arm64 build in `Check-Docker`. The `CLAUDE.md` note explains why.
- **[Rollback on an arm64 node to 1.2.1 or earlier fails]** → Accepted. Those tags stay amd64-only, and the README says which releases are multi-platform.
- **[Default chart installs don't get this image]** → Out of scope (see proposal.md). `helm/values.yaml` points at another registry path.

## Migration Plan

No data or consumer migration. A released tag keeps its name, and a client on amd64 still gets an amd64 image.

1. Push the Dockerfile, workflow and doc changes to `main`. `Check-Docker` runs on that push and proves both builds on a runner.
2. The next `v*` tag publishes the first multi-platform image.

**Rollback:** revert the commit. The following tag is amd64-only again, and already published multi-platform tags are unaffected.
