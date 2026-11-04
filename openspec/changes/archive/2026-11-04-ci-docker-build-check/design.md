## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach:

- **Workflows that build or react to images:**
  - `Build-Docker` builds the image only on `v*` tags and manual runs. It uses `docker/build-push-action@v7` with `context: ./`. It has no `docker/setup-buildx-action`, so the action uses the runner's default builder.
  - `Build-Helm` starts on `workflow_run: workflows: [Build-Docker]`, matched by **name**, on every completed run. If that run succeeded, `Build-and-Push-Helm` publishes the chart, because its condition is only `github.event_name == 'workflow_run'`.
  - `Build-Dotnet` runs on pushes and pull requests to `main` (paths `src/**`, `tests/**`, `global.json` and its own file). `Build-Docker`'s `Test` gate also calls it through `workflow_call`.
- **Runner builder (from the `v1.2.0` release log, run 41692964753):**
  - Docker Server 29.4.2 with the containerd image store (`driver-type: io.containerd.snapshotter.v1`);
  - "building with "default" instance using docker driver";
  - the pushed tag is an OCI image index (`application/vnd.oci.image.index.v1+json`) with provenance attestation manifests.
- **Build size** (local, `restructure-source-layout` task 2.3):
  - build context 14.5 kB;
  - `dotnet restore` 9.7 s, `dotnet publish` 5.1 s;
  - most of the remaining time goes to pulling the base images.
- **Reference: EuGo-docint's `ci.yml`.** Its `docker` job runs on pushes and pull requests to `main`:
  - `needs: build-test`;
  - QEMU and `docker/setup-buildx-action`;
  - `build-push-action` with `push: false`, `platforms: linux/amd64`, `cache-from` / `cache-to: type=gha`.
- **Work split.** Workflow and documentation changes stay in the main session (`CLAUDE.md`). All commits go to `main`; no branches or PRs.

## Goals / Non-Goals

**Goals:**
- A change that breaks the image build fails a check on the push or pull request that introduces it, instead of at the next release.
- The check exercises the same build as a release: the same Dockerfile, build context, action and builder.
- Nothing a check run does can publish an image or a chart.

**Non-Goals:**
- **Running the container in CI.** Endpoints, read-only and non-root start, and `/App` contents stay manual checks (see D2).
- **A layer cache, buildx, or any change to `Build-Docker`** (see D3).
- **Multi-arch builds.**
- **Gating the check on `Build-Dotnet`'s tests.**
- **Branch protection or required status checks.**

## Decisions

Each decision is marked *agreed* (decided with the user during exploration) or *planning default* (proposed, open to change at review).

### D1. A separate workflow named `Check-Docker` (agreed)

`.github/workflows/check-docker.yaml`, with `name: Check-Docker`. The name pairs with `Build-Helm`'s `Check-Helm` job and can't be confused with `Build-Docker`.

Alternatives rejected:
- **A job in `build-dotnet.yaml`.** `Build-Docker`'s `Test` gate calls that workflow through `workflow_call`, so every release would build the image twice.
  - A guard on the event or ref doesn't help, because a called workflow sees the caller's `push` event. It would need an input instead.
  - The job would also share `Build-Dotnet`'s path filters. A `tests/**` change would build the image, and a Dockerfile change would run the .NET build and tests.
- **A `push: main` trigger on `Build-Docker`.** Every check run would be a completed `Build-Docker` run, so `Build-Helm` would publish the chart from every push to `main`. That breaks `ci-test-gate`'s "Only release runs publish a chart".
- **A new workflow named `Build-Docker`.** It has the same problem: `Build-Helm` matches by name.

### D2. Build only, no container run (agreed)

The workflow builds the image and stops. That matches the scope of EuGo-docint's check.

**Alternative considered: build, then run.** The workflow would:
- start the image read-only as 1000:1000;
- probe `/`, `/healthz`, `/readyz` and `/livez`;
- list `/App` for test files.

That would put three `service-runtime` scenarios under CI, at roughly 30 s per run. The user chose build-only. It stays a possible later change.

### D3. No layer cache, no buildx; `Build-Docker` untouched (agreed)

The check uses the runner's default builder. That is also what `Build-Docker` uses, so the check builds the image the same way a release does.

- **Evidence:** the cache could save at most the restore and publish steps, about 15 s.
- **Scope rule:** a tag run can only read caches saved by its own tag or by the default branch. GitHub's docs say runs "cannot restore caches created for different tag names". Entries unused for 7 days are deleted.
- **Upload cost:** `cache-to: type=gha,mode=max` uploads the restore layer on every run, which would cancel most of the saving.

Alternatives rejected:
- **EuGo-docint's full pattern:** buildx plus `cache-from` and `cache-to` in both workflows.
- **A trimmed version:** `cache-to` only from pushes to `main`, `cache-from` only in the release.

Both would add a builder and cache settings to the release workflow for a saving of seconds. If the image ever becomes heavy, slow or multi-arch, the trimmed version is a self-contained follow-up.

Adding buildx later would not change what a release pushes. The default builder already pushes an OCI index with provenance attestations (see Context).

### D4. Triggers and paths (planning default)

```yaml
on:
  workflow_dispatch:
  push:
    branches: [main]
    paths: &image-paths
      - 'Dockerfile'
      - '.dockerignore'
      - 'global.json'
      - 'src/**'
      - '.github/workflows/check-docker.yaml'
  pull_request:
    branches: [main]
    paths: *image-paths
```

The anchor is shown for brevity. The file repeats the list explicitly, matching `build-dotnet.yaml`.

- **Why this list:** it is exactly what the Dockerfile reads (`global.json`, `src/ExampleNet/`), plus the build definition itself.
- **`src/**` rather than `src/ExampleNet/**`.** A future project under `src/` that the app references would also need a Dockerfile change. A check on `src/**` catches that the image no longer builds.
- **Not included:** `tests/**`, which `.dockerignore` excludes from the build, and `helm/**`.
- **`workflow_dispatch` is safe.** A manual run builds without pushing, and its name doesn't match `Build-Helm`'s trigger.
- **Tags don't trigger it.** `branches: [main]` excludes tag pushes, so a release runs only `Build-Docker`, as before.

### D5. The build step (planning default)

Two steps:
1. `actions/checkout@v7`;
2. `docker/build-push-action@v7` with:
   - `context: .`
   - `push: false`
   - `tags: example-net-docker:check`

Supporting choices:
- **The action is the same one `Build-Docker` uses.** Plain `docker build` was the alternative; using the action keeps the check and the release on the same tool and builder.
- **Permissions are read-only:** top-level `permissions: contents: read`.
- **No registry login,** and no `packages` or `id-token` permission. The workflow can't push or sign even if someone sets `push: true` by mistake.
- **The tag** `example-net-docker:check` is local to the runner and never leaves it.

### D6. Runs in parallel with `Build-Dotnet` (planning default)

A separate workflow can't `needs:` a job in another workflow. EuGo-docint gates its Docker job on its tests within one workflow. Here, nothing in the check publishes, so waiting for tests would only delay a separate signal:
- **`Build-Dotnet`:** do the tests pass?
- **`Check-Docker`:** does the image still build?

A push to `src/**` runs both at once.

### D7. Documentation (planning default)

The `CLAUDE.md` CI/CD section gets a `Check-Docker` entry that says:
- its triggers, and that it builds only and publishes nothing;
- that it must never be named `Build-Docker` (see D1);
- that any new root-level file the Dockerfile copies (a future `Directory.Build.props`, `Directory.Packages.props` or `NuGet.config`) must also be added to `Check-Docker`'s paths. This extends the existing `COPY` rule in the Container build note.

The README isn't changed. Its GitHub Actions section describes releases only.

## Risks / Trade-offs

- **[A rename to `Build-Docker` would publish charts from every push]** → D1 picks a distinct name, `CLAUDE.md` records the constraint, and a live task checks that no `Build-Helm` run follows a `Check-Docker` run.
- **[The check passes but a release fails for a release-only reason]** (registry login, metadata, push, signing, the chart bump) → out of scope. The check proves only that the image builds; the release steps are still first exercised on a tag.
- **[The paths miss a file that affects the image]** → D4 covers everything the Dockerfile reads today, and D7 makes adding a root-level build file a two-place change: the Dockerfile `COPY` and `Check-Docker`'s paths.
- **[The base image tag moves between a check and a release]** → accepted. `sdk:10.0` and `aspnet:10.0-noble-chiseled` are floating tags by design, and the release pulls them fresh.
- **[Extra runner time]** → about one minute per push or pull request to `main` that touches the image paths.
- **[A failing build can't be shown live]** → the repository takes no PRs, and a broken Dockerfile can't be pushed to `main` without breaking it. The failure scenario is checked locally instead (see tasks).

## Migration Plan

No migration. Adding the workflow file is the whole rollout. Rollback means deleting `.github/workflows/check-docker.yaml` and its `CLAUDE.md` entry.
