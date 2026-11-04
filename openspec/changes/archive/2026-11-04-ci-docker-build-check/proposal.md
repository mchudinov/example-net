## Why

The container image is built in only one place: `Build-Docker`, which runs on `v*` tags and manual runs. A broken Dockerfile therefore first shows up at release time.

That failure is safe:
- the `Test` gate passes;
- `Build-and-Push-Docker` fails;
- nothing is pushed or signed, and `Chart.yaml` is not bumped.

It still means fixing the Dockerfile and pushing the next patch tag. The archived `restructure-source-layout` change rewrote the Dockerfile's build stage and added a `.dockerignore`. So far only local builds have checked them; the next release will be their first CI build.

The archived `migrate-tests-to-mstest-sdk` design listed "Building the Docker image in PR CI" as a non-goal. This change reverses that. It follows EuGo-docint, whose `ci.yml` builds the image without pushing it on every push and pull request to `main`.

## What Changes

- **New workflow `Check-Docker`** (`.github/workflows/check-docker.yaml). It builds the container image with `push: false`. It runs on pushes and pull requests to `main` that change any of these:
  - `Dockerfile`
  - `.dockerignore`
  - `global.json`
  - `src/**`
  - its own workflow file

  It can also be started manually. Changes only under `tests/**` don't trigger it, because `.dockerignore` keeps `tests/` out of the image build. Changes only under `helm/**` don't trigger it either.
- **Build only.** The check proves that the image builds. It doesn't start the container and doesn't inspect its contents; the endpoint, security-context and image-content checks stay manual, as before.
- **Publishes nothing:**
  - no registry login;
  - read-only permissions;
  - not named `Build-Docker`. `Build-Helm` starts after any completed `Build-Docker` run and publishes the chart if it succeeded, so a check named `Build-Docker` would publish the chart.
- **No layer cache.** The build uses the runner's default builder, the same one `Build-Docker` uses, with no `docker/setup-buildx-action` and no GitHub Actions cache.
- **Releases stay as they are.** `Build-Docker`, `Build-Dotnet` and `Build-Helm` don't change. Every `v*` tag still runs:
  1. the `Test` gate;
  2. build, push and sign the image;
  3. the bot's `Chart.yaml` bump;
  4. `Build-Helm` publishing and signing the chart.
- **Docs:** `CLAUDE.md` describes `Check-Docker` in its CI/CD section.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that pushes and pull requests to `main` that affect the container image are checked by building the image, and that this check publishes nothing.

## Impact

- **CI:** new `.github/workflows/check-docker.yaml`.
- **Docs:** `CLAUDE.md`, CI/CD section.
- **Unchanged:** `Build-Docker`, `Build-Dotnet`, `Build-Helm`, the `Dockerfile`, `.dockerignore`, the application code, the Helm chart and `docker-compose.yml`.
- **Cost:** about one minute of runner time for each push or pull request to `main` that touches the paths above. Pushes to `src/**` also run `Build-Dotnet`; the two workflows run in parallel.
- **Depends on `restructure-source-layout`** (archived), for the `src/` layout, the new Dockerfile and `.dockerignore`.
