## Why

The Helm chart's default image is `ghcr.io/wss-ea-cloud-devops/example-net-docker`, but this repo's releases push to `ghcr.io/mchudinov/example-net-docker`, the correct location. So a default chart install can't get any image this repo publishes, on any architecture. CI doesn't notice, because the chart's test install doesn't wait for its pods.

Correcting the path isn't enough on its own. The image and chart packages are meant to be public, but on 2026-11-06 the registry refused anonymous requests for both (401), while a known public image on the same endpoint returned 200. They have to be made public, after which the chart needs no pull secret.

## What Changes

- **Chart default image:** `image.repository` in `helm/values.yaml` becomes `ghcr.io/mchudinov/example-net-docker`.
- **Chart default pull secret:** `imagePullSecrets` in `helm/values.yaml` becomes empty. The templates already skip the field when it's empty, and a deployer can still set a secret.
- **Package visibility (a GitHub setting, not a repo edit):** the owner makes `example-net-docker` and `example-net-helm` public, before the next `v*` tag. GitHub's docs say a public package can't be made private again.
- **`pod-example.yaml`:** uses the correct path and tag `1.2.1`, and drops its pull secret. This repo has no `0.2.10` tag, so changing only the path would leave a tag that doesn't resolve.
- **`build-docker.sh` is deleted.** It pushes a hand-built `0.1.0` to the old path, and nothing references it. Pointing it at the correct path would let it put an unsigned, amd64-only image into the release path, which only `Build-Docker` should write to.
- **`README.md`:** names `mchudinov` as the destination, uses `oci://ghcr.io/mchudinov/example-net-helm` in the install command, and drops the pull secret prerequisite, the request to the Cloud&DevOps team and the GHCR login step. It also says that chart 1.2.1 and earlier need `image.repository` set, because until the next release the install command still gets chart 1.2.1.
- **`CLAUDE.md`:** the chart section says the default image must match the path releases push to, and no longer lists the pull secret as a cluster prerequisite.
- **BREAKING for upgrades that rely on the old default:** a deployment that takes `image.repository` from the chart and pulls a copy of the image from the old path will pull from `ghcr.io/mchudinov/example-net-docker` after upgrading to the next chart. Deployments that set `image.repository` themselves aren't affected.

**Out of scope:**
- A CI check that compares the chart default with the release path.
- Making the chart's test install wait for its pods, or running the image in CI. This keeps the earlier "no smoke tests" decision.
- Replacing published charts. Chart 1.2.1 and earlier keep the old default, so installing one of them still needs `image.repository` overridden.
- `.github/workflows/`. Both release workflows already derive the path from the repo owner, and `Build-Helm`'s GHCR login is for pushing.
- `README.md:191-193`, which cover cluster prerequisites that aren't about the image.
- The archived `add-arm64-image` change, which stays as a historical record.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `service-runtime`: adds requirements that the chart's default image is the one releases publish, and that the published image and chart can be pulled without credentials.

## Impact

- **Chart:** `helm/values.yaml`. The templates don't change.
- **Example manifest:** `pod-example.yaml`.
- **Removed:** `build-docker.sh`.
- **Docs:** `README.md` and `CLAUDE.md`.
- **GitHub settings:** the visibility of both packages, changed by the owner. This step is one-way.
- **CI:** pushing `helm/values.yaml` runs the `Build-Helm` checks, which publish nothing. No workflow changes.
- **Unchanged:** the application code, the tests, `Dockerfile`, `docker-compose.yml` and the workflows. With no C# or project file changes, all the work stays in the main session.
- **When it takes effect:** the published chart gets the new defaults with the next `v*` tag. Until then, `main`'s chart renders `ghcr.io/mchudinov/example-net-docker:1.2.1`, which is amd64-only.
