All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main`.

No workflow or verification step starts the container, on either platform (design D2). Task 6.2 only records the result of the first real arm64 deployment. The Docker daemon wasn't running during planning, so start it before section 1. If it's unavailable, record the affected tasks as **not run**.

`<scratchpad>` is the session's scratchpad directory. Pass it to `docker buildx` as a Windows-style path, because Git Bash doesn't convert a path inside `--output`.

## 1. Dockerfile (design D3, D4)

- [x] 1.1 Before editing, capture the current amd64 payload: `docker buildx build --platform linux/amd64 --output type=local,dest=<scratchpad>/baseline-amd64 .`. Verify that `<scratchpad>/baseline-amd64/App/ExampleNet.dll` and `web.config` exist, then save the sorted `/App` listing for task 1.4.

  *Result (2026-11-06):* the command as written doesn't work on Windows. `type=local` has to recreate the chiseled image's symlinks and stops with `failed to symlink bin: ... A required privilege is not held by the client`, leaving empty files. That partial output was discarded. Tasks 1.1 and 1.3 used `--output type=tar,dest=<file>` and extracted only `App/` with `tar --force-local`, which checks the same thing without starting a container.

  The baseline `/App` has 21 files, including `ExampleNet.dll` and `web.config`. The apphost is ELF x86-64, the DLL is AnyCPU (`0x014c`), and the `deps.json` runtime target is `.NETCoreApp,Version=v10.0`.
- [x] 1.2 Change the `Dockerfile` build stage:
  - `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env`, followed by `ARG TARGETARCH`;
  - add `-a $TARGETARCH` to `dotnet restore` and `dotnet publish`;
  - add a short comment saying the SDK runs natively and cross-compiles, and that `-a` is needed on both commands;
  - update the restore layer comment, because each architecture now has its own restore layer.

  Verify:
  - `grep -cE '^RUN dotnet (restore|publish) .*-a \$TARGETARCH' Dockerfile` returns `2`.
  - `git diff Dockerfile` shows no change from the runtime stage's `FROM` line onward, and the runtime stage has no `RUN`.

  *Result:* the grep returns `2`. The runtime stage is byte-identical to `HEAD` from its `FROM` line onward and has no `RUN`.
- [x] 1.3 Export both new payloads, without starting anything: run `docker buildx build --platform <platform> --output type=local,dest=<scratchpad>/new-<arch> .` for `linux/amd64` and `linux/arm64`. Verify (spec: "Each image contains no binary built for another architecture", "Both platforms ship the same files", "Container image contains only the application"):
  - `od -A n -t x1 -j 18 -N 2 App/ExampleNet` shows `3e 00` for amd64 and `b7 00` for arm64.
  - The PE machine field of `App/ExampleNet.dll` is `0x8664` for amd64 and `0xaa64` for arm64.
  - `App/ExampleNet.deps.json` has the runtime target `.NETCoreApp,Version=v10.0/linux-x64` for amd64 and `.NETCoreApp,Version=v10.0/linux-arm64` for arm64.
  - The two sorted `/App` listings are identical.
  - Neither `/App` has `ExampleNetTests.*`, `MSTest*`, `testhost.*`, `Microsoft.TestPlatform.*`, `Microsoft.Testing.*`, `coverlet.*` or `Microsoft.CodeCoverage` entries.

  *Result (exported as tar, see task 1.1):*

  | Check | amd64 | arm64 |
  |---|---|---|
  | `App/ExampleNet` `e_machine` | `3e 00` | `b7 00` |
  | `App/ExampleNet.dll` PE machine | `0x8664` | `0xaa64` |
  | `deps.json` runtime target | `.../linux-x64` | `.../linux-arm64` |
  | Other 13 DLLs | AnyCPU (`0x014c`) | AnyCPU (`0x014c`) |
  | Files in `/App` | 20 | 20 |
  | Test payload entries | 0 | 0 |

  A scan of every ELF and PE file in each `/App` found nothing built for the other architecture, and the two listings are identical. The base image's `usr/share/dotnet/dotnet` host is also aarch64 in the arm64 export. The local builder lists only `linux/amd64` variants, so the arm64 build ran without emulation. No container was started.
- [x] 1.4 Compare the new amd64 `/App` listing with the baseline from task 1.1. Verify that the only difference is the missing `web.config` (design D4). If anything else differs, stop and report it before continuing.

  *Result:* the only difference is `./web.config` (21 files before, 20 after).
- [x] 1.5 Check the local build paths: run `docker build -t example-net-docker:check .` and `docker compose build`. Verify that both succeed, then remove the images they created. Don't use `docker compose up`, because it starts the container.

  *Result:* both builds exit 0, and `example-net-docker:check` was removed.
  - **Exception:** `example-net-example-net:latest` existed before this task, and `docker compose build` moved the tag to the new build. The containerd store dropped the old image (`2fb9f91d8a8e`), so it couldn't be restored. The tag was kept on the new build rather than removed.
  - **Extra check, beyond the task:** `docker buildx build --platform linux/amd64,linux/arm64 --load .` on the local builder, which has the same driver and image store as the runner, exits 0. It exports a manifest list with `linux/amd64` and `linux/arm64`. The image was removed afterwards. Task 5.1 is still what proves this on a runner.

## 2. Workflows (design D5)

- [x] 2.1 In `.github/workflows/build-docker.yaml`, add `platforms: linux/amd64,linux/arm64` to the "Build and push Docker image" step, with a comment saying the list must match `check-docker.yaml`. Verify that `git diff` shows only those lines, and that the `Sign-Docker-Image` job is unchanged (design D6).

  *Result:* one hunk, two added lines. The `Sign-Docker-Image` job is identical to `HEAD`.
- [x] 2.2 In `.github/workflows/check-docker.yaml`, add the same `platforms` line to the "Build image (not pushed)" step, with a comment saying the list must match `build-docker.yaml`. Verify:
  - `grep -h 'platforms:' .github/workflows/build-docker.yaml .github/workflows/check-docker.yaml` prints two identical values.
  - `grep -nE "login-action|setup-buildx|setup-qemu|cache-(from|to)|push: true|packages:|id-token" .github/workflows/check-docker.yaml` returns nothing.
  - The workflow is still named `Check-Docker`.

  *Result:* both `platforms` lines are identical, the forbidden-pattern grep returns nothing (exit 1), and the name is unchanged.
- [x] 2.3 From the root, in Git Bash, run `MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/repo" -w /repo rhysd/actionlint:latest`. Verify that `check-docker.yaml` has 0 findings and that the only other findings are the six pre-existing `SC2086` notes in `build-docker.yaml` and `build-helm.yaml`.

  *Result:* actionlint 1.7.12 reports 0 findings in `check-docker.yaml`. The only findings are the six `SC2086` notes: three at `build-docker.yaml:54` and three at `build-helm.yaml:46`.

## 3. Documentation (design D7)

- [x] 3.1 Update `CLAUDE.md`:
  - **Container build note:** the build stage cross-compiles with `--platform=$BUILDPLATFORM` and `-a $TARGETARCH`, and the runtime stage must stay free of `RUN`;
  - **Target framework bullet:** a new runtime image must include `linux/arm64`;
  - **Check-Docker and Build-Docker entries:** both build `linux/amd64` and `linux/arm64`, and the two lists must match.

  Verify by reading the diff: every statement matches the edited Dockerfile and workflows, and the "default builder" wording still holds unless task 5.2's fallback was used.

  *Result:* the diff was read against the edited files and matches. The "default builder" wording stays, subject to task 5.1. The `Check-Docker` entry also says that it runs no container, which follows from design D2.
- [x] 3.2 Update the README's GitHub actions section to say images are published for `linux/amd64` and `linux/arm64`, starting with the first release after 1.2.1. Verify by reading the diff that nothing else in the README changed.

  *Result:* only that paragraph was added.

## 4. Scope check

- [x] 4.1 Verify that `git status --short` shows no change under `src/`, `tests/` or `helm/`, or to `global.json`, `.dockerignore`, `docker-compose.yml`, `.github/workflows/build-dotnet.yaml` or `.github/workflows/build-helm.yaml`.

  *Result:* the only changes are `Dockerfile`, `build-docker.yaml`, `check-docker.yaml`, `CLAUDE.md`, `README.md` and this change's directory.
- [x] 4.2 Run `openspec validate add-arm64-image --strict` and verify that it passes.

  *Result:* `Change 'add-arm64-image' is valid`.

## 5. Check on a runner (design D5)

- [x] 5.1 Ask the user before pushing, then commit on `main` and push. Verify (spec: "Image build check covers every published platform", "Image builds for a change to its build definition"):
  - A `Check-Docker` run starts on that push and succeeds.
  - Its log shows the image being built for both `linux/amd64` and `linux/arm64`, with no emulation set up.
  - `gh run list` shows no `Build-Docker` or `Build-Helm` run started by that push, so nothing was published.

  *Result:* the user committed and pushed `8f8e9bf`. `Check-Docker` run 42087897329 succeeded, and its `Build-Image` job took 26 s.
  - **Builder:** `default` with the `docker` driver and the containerd store. It lists only `linux/amd64` variants, so no emulation was available.
  - **Platforms:** the command has `--platform linux/amd64,linux/arm64`. The log shows `[linux/amd64 build-env ...]` and `[linux/amd64->arm64 build-env ...]` for both restore and publish, then one exported manifest list.
  - **Publishing:** `Check-Docker` is the only run for that commit. The latest `Build-Docker` and `Build-Helm` runs predate it.
- [x] 5.2 Only if task 5.1 fails because the default builder rejects multi-platform: apply the fallback in design D5, adding `docker/setup-buildx-action` to both workflows and updating the wording in `CLAUDE.md` and the `check-docker.yaml` comment, then push again after asking. Verify that `Check-Docker` then succeeds as in task 5.1. If the fallback isn't needed, record that and mark this task done.

  *Result:* not needed. The default builder accepted both platforms in task 5.1.

## 6. First multi-platform release

- [x] 6.1 The user decides when to push the next `v*` tag. After that release, log in with `docker login ghcr.io` using a token that has `read:packages`. Verify (spec: "Version tag lists both platforms", "Signature verifies for the released image"):
  - `docker buildx imagetools inspect ghcr.io/mchudinov/example-net-docker:<version>` lists `linux/amd64` and `linux/arm64`.
  - The digest in `Sign-Docker-Image`'s "Sign the image" step is the digest the tag resolves to, and "Verify the signature" succeeded.
  - `Build-Helm` published and signed the chart for that version.

  If no tag has been pushed by archive time, record this as **pending at archive**, with the evidence from tasks 1.3 and 5.1, as `restructure-source-layout` did.

  **Pending at archive** (2026-11-06): no `v*` tag has been pushed since `8f8e9bf`. Evidence so far: each platform's payload targets its own architecture (1.3), and `Check-Docker` run 42087897329 built both platforms on a runner (5.1). The multi-platform push, the signing of its digest and the chart publish first run on the next tag.

  *Observed after archive (2026-11-06):* release `v1.3.0`, `Build-Docker` run 42220698024 and `Build-Helm` run 42220906429, both successful. The packages had become public by then, so no login was needed. `docker buildx imagetools inspect` lists `linux/amd64` and `linux/arm64`, plus two attestation manifests. The tag resolves to `sha256:f47218b5…186a`, the same digest used in "Sign the image", and "Verify the signature" succeeded. `Build-Helm` pushed `example-net-helm:1.3.0` (`sha256:662a06b3…c18c`), and its signing and verification steps succeeded.
- [ ] 6.2 When the image is first deployed to an arm64 node, record the version and whether the pod became ready. This is the first proof that the arm64 image starts (design D2). Verify by noting the result here. If there's been no arm64 deployment by archive time, record this as **pending at archive**.

  **Pending at archive** (2026-11-06): no multi-platform image has been released or deployed yet, and neither platform's new image has been started, under the user's "no smoke tests" decision (design D2). The deployment needs `image.repository` overridden, because the chart's default points at `ghcr.io/wss-ea-cloud-devops/example-net-docker` (see proposal.md).

  *Observed after archive (2026-11-06):* still open, because no arm64 deployment has happened. From chart 1.3.0 the override isn't needed: `fix-chart-image-location` corrected the default.
