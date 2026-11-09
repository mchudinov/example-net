## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-09:

- **Branch references.** Exactly three `uses:` lines name a branch: `build-docker.yaml:101` and `build-helm.yaml:177` (`sigstore/cosign-installer@main`), and `build-helm.yaml:120` (`setup-polaris@master`).
- **cosign-installer.** `main` and `v4.1.3` are the same commit, `756fe2d284c7292c4e9b62c8a28f1e5629c59532`. `v4.1.3` is a GitHub immutable release (`immutable: true`), so its tag can't be moved. Its `cosign-release` input defaults to `v3.1.4`, and the workflows don't set that input.
- **setup-polaris.** The action's folder compared across refs:

  ```
                     8.5.5         v10.2.6       master
  action.yaml        same          same          same
  get_polaris.sh     same          same          same
  Dockerfile base    alpine:3.17   alpine:3.24   alpine:3.24
  ```

  The other 130 commits between 8.5.5 and `master` are elsewhere in the repo. `master` is 4 commits ahead of `v10.2.6` (`6e76dd36aea6a5d8dd380ddf3e9199a83d252c55`), and the action's folder last changed on 2026-08-06 (`cb741e3eb1`), before that tag. Polaris releases aren't immutable.
- **What the action's ref decides.** `get_polaris.sh` downloads `releases/download/$INPUT_VERSION/polaris_linux_amd64.tar.gz`, so the `version` input picks the binary and the ref only picks the installer.
- **Job tokens.** `Sign-Docker-Image` and `Sign-Helm-Chart` hold `id-token: write` and `packages: write`. `Check-Helm`, where Polaris runs, has no `permissions:` block, and the repo's default workflow permission is `read`.
- **What exercises each line.** `Build-Helm` triggers on its own workflow file, so a push runs `Check-Helm`, including `Setup Polaris`. `Sign-Helm-Chart` runs only after a release, and `Build-Docker` only on a `v*` tag or a manual run. No push reaches either cosign line.
- **Update tooling.** The repo has no `.github/dependabot.yml`.
- **Limit of this evidence.** `get_polaris.sh` and both Dockerfiles were read at the refs shown. `action.yaml` at `v10.2.6` was not read directly. It follows from the folder's last change predating that tag. Neither pin has been run, because planning stayed read-only.

## Goals / Non-Goals

**Goals:**
- No third-party `uses:` line can change what it runs because a branch moved.
- Neither pin changes what runs today.

**Non-Goals:**
- **Making every dependency immutable.** Major-version tags and files downloaded at run time stay as they are (see D1, D5).

## Decisions

Each decision is marked *agreed* (decided with the user) or *planning default* (proposed, open to change at review).

### D1. Close branch references only (agreed)

Three lines change. A branch is the reference that moves on every upstream commit, so it's the minimum to fix.

**Alternatives considered:**
- **SHAs for every action.** It's the strongest form, but it's a large diff, and with no update tooling every line would need bumping by hand.
- **Exact tags for every action.** It's a smaller step than SHAs, and has the same bumping cost for a benefit outside this change's scope.

### D2. `cosign-installer` is pinned to `@v4.1.3` (agreed)

`v4.1.3` is an immutable release, so the tag is as strong as a SHA here and easier to read. It's the commit that runs today.

The `cosign-release` input stays unset, so the binary is that release's default, v3.1.4, and moves only when the pin does.

**Alternatives considered:**
- **The full SHA.** It adds no protection over an immutable release.
- **Setting `cosign-release` explicitly.** It's a second value to keep in step, in two files.

### D3. `setup-polaris` is pinned to `@v10.2.6` (planning default)

This departs from the brief, which proposed `@8.5.5` because it matches `POLARIS_VERSION`. The ref only picks the installer (see Context), so the match has no effect. `@v10.2.6` keeps the action exactly as it runs today, and `@8.5.5` would move its container back to `alpine:3.17`, which went out of support in November 2024.

The line gets a comment saying the ref and `POLARIS_VERSION` are independent, because they will look like a mismatch.

**Alternatives considered:**
- **`@8.5.5`.** It's the same installer logic on an unsupported base image.
- **The SHA `6e76dd36aea6a5d8dd380ddf3e9199a83d252c55`.** It's the only form that can't change, since Polaris releases aren't immutable. A tag is enough here because the step's token is read-only, so a moved tag could fake a passing audit but couldn't publish or sign.
- **Downloading the binary in a `run:` step.** It drops the action, but it's more than a pin.

### D4. `POLARIS_VERSION` stays at 8.5.5 (planning default)

The binary is two major versions behind the latest release, but a newer one can change findings and the score the audit gates on. That needs its own change.

### D5. Run-time downloads stay as they are (agreed for the signing config, planning default for the rest)

The signing config is meant to be kept current by Sigstore. It's also data, not code: a tampered file should make the `cosign verify` step fail rather than let a bad signature through. That's from how cosign checks a certificate against its trust root, and wasn't tested.

k3d's `install.sh` and the unchecked Polaris binary download are the same kind of exposure, and need different fixes from a `uses:` pin.

### D6. This change doesn't cut a release (planning default)

A tag would publish a new image and chart only to prove two lines that resolve to the commit already running. The next release that happens for its own reasons exercises them.

### D7. The rule goes in `CLAUDE.md`, with no CI gate or Dependabot (planning default)

`CLAUDE.md` is where this repo keeps CI rules, and it's read by whoever edits the workflows next.

**Alternatives considered:**
- **A step that fails on `@main` or `@master`.** It enforces the rule, but it's a new check to maintain, and this change already removes every line it would catch.
- **Dependabot for `github-actions`.** It would keep the pins current, but it opens pull requests and this repo works straight on `main`.

### D8. No branch or pull request (agreed)

The work goes directly on `main`, as this repo does. The user is asked before the push.

## Risks / Trade-offs

- **[A fixed cosign reads a signing config that still moves]** It's the arrangement that broke k3d, a fixed version with a moving download. The filename carries a schema version (`v0.2`), which makes a breaking change to that file unlikely. → If it happened, signing fails after the image is pushed, and the fix is the next patch tag.
- **[The cosign pins first run in a release]** No push reaches them. → They resolve to the commit running today, and task 1 confirms that immediately before the edit.
- **[The pins go stale]** Cosign stays at v3.1.4 and picks up no fixes until someone edits two lines. → Accepted, and revisit Dependabot if it becomes a chore.
- **[The Polaris tag can be moved]** It isn't an immutable release. → Accepted because the step's token is read-only.
- **[That read-only token comes from a repo setting]** `Check-Helm` has no `permissions:` block, so changing the repo default would widen it. → Not changed here.
- **[Major tags still float in the signing jobs]** `docker/login-action@v4` runs in both signing jobs, with the same token as cosign. → Outside this change (D1), and listed so it's a known choice.
- **[Nothing stops a new branch reference]** There's no CI gate. → `CLAUDE.md` states the rule (D7).

## Migration Plan

1. The repo changes land on `main`. `Build-Helm` runs the chart checks with the pinned Polaris installer and publishes nothing.
2. The next `v*` tag, whenever one is due, is the first run of both cosign pins.

**Rollback:** revert the commit. Published images and charts aren't affected, because neither pin changes what they're built from.
