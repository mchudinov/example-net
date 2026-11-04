## Why

The container image is built in only one place: `Build-Docker`, on `v*` tags and manual runs. A broken Dockerfile therefore first shows up at release time.

That failure is safe: the `Test` gate passes, `Build-and-Push-Docker` fails, and nothing is pushed or signed and `Chart.yaml` is not bumped. It still means fixing the Dockerfile and pushing the next patch tag. `restructure-source-layout` rewrites the Dockerfile's build stage, which makes this gap matter more.

The archived `migrate-tests-to-mstest-sdk` design listed "Building the Docker image in PR CI" as a non-goal. This change reverses that.

Second, `restructure-source-layout` gives the NuGet restore its own Docker layer, but that layer is reused only on local machines. A GitHub Actions layer cache (`type=gha`) helps release builds only if something on `main` fills it. GitHub's cache rules:
- a run can restore cache entries saved by its own branch or tag, or by the default branch;
- it "cannot restore caches created for different tag names";
- entries are deleted after 7 days without use.

So each `v*` release can read only what `main` saved. EuGo-docint works this way: its `ci.yml` runs a `push: false` Docker build on `main` with `cache-to: type=gha,mode=max`, and its `release.yml` reads that cache.

## What Changes

- **New Docker build check.** On pushes and pull requests to `main`, build the image without pushing it (`push: false`). It runs when any of these change:
  - `Dockerfile`
  - `.dockerignore`
  - `global.json`
  - `src/**`
  - the workflow file that holds the check

  Changes under `tests/**` don't trigger it, because `.dockerignore` keeps `tests/` out of the image build.
- **Layer cache.** Add `cache-from: type=gha` and `cache-to: type=gha,mode=max` to the check build and to `Build-Docker`'s build-and-push step.

  Both jobs add `docker/setup-buildx-action`, which `Build-Docker` doesn't run today; EuGo-docint runs it in both of its jobs. Without it the build uses the runner's default Docker driver, which usually can't export a GitHub Actions cache. Newer engines that use the containerd image store can, so the design should confirm the runner's behavior. Adding buildx is the safe choice either way.
- **Spec:** `ci-test-gate` gains a requirement that every change to `main` affecting the container image is checked by building the image, without publishing anything.
- **Unchanged: releases still build on tags.** Every `v*` tag still runs `Build-Docker` the same way:
  1. the `Test` gate;
  2. build, push and sign the image;
  3. the bot's `Chart.yaml` bump;
  4. `Build-Helm` publishes and signs the chart.

  The only additions to `Build-Docker` are the buildx setup and the two cache lines. The check build never pushes, signs or publishes anything, and must not trigger `Build-Helm`.

### Open question (resolve in design)

Where should the check job live? The answer decides where the path filters and cache settings go.
- **A job in `build-dotnet.yaml`.** It reuses that workflow's triggers. However, `Build-Docker`'s `Test` gate calls `build-dotnet.yaml` through `workflow_call`, so every release would build the image twice unless the job is guarded, for example by an input. `build-dotnet.yaml`'s path filters would also have to add `Dockerfile` and `.dockerignore`.
- **A separate workflow.** It keeps the release gate unchanged. It must not be named `Build-Docker`, because `Build-Helm` finds that workflow by name.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that changes to `main` affecting the container image are checked by an image build on pushes and pull requests, and that this check publishes nothing.

## Impact

- **CI:**
  - a new job or workflow for the check (see the open question);
  - `.github/workflows/build-docker.yaml`: `docker/setup-buildx-action` plus the two cache lines.
- **Unchanged:**
  - the release triggers, jobs and order;
  - the `Dockerfile`, application code, Helm chart and `docker-compose.yml`.
- **Cost:**
  - one image build per push or pull request to `main` that touches the paths above;
  - cache entries count against the repository's Actions cache quota.
- **Depends on `restructure-source-layout`**, for the `src/` paths, the new Dockerfile and `.dockerignore`. Apply this change after that one.
