## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-06:

- **Release path.** `Build-Docker` and `Build-Helm` both push under `ghcr.io/${{ github.repository_owner }}`, which is `mchudinov` for this repo. `Build-Docker` also pushes the tags `1.2.1` and `1.2` for that release.
- **Chart default.** `helm/values.yaml:5` hard-codes the old organization path, unchanged since 2023. The repo was created in May 2026 and isn't a fork. Both the Deployment and the blue-green Rollout build the image reference from this value, with the tag falling back to the chart's `appVersion`.
- **Pull secret.** `helm/values.yaml:9` names `container-registry-pullsecret`. Both templates wrap the field in `with`, so an empty list renders no `imagePullSecrets`, and a deployer's list still renders. Both results were checked with `helm template`.
- **Why CI never noticed.** The test install at `build-helm.yaml:82` has no `--wait`, and Polaris has no registry rule, so an image that can't be pulled doesn't fail the run.
- **Package visibility.** The repo is public, but both packages refuse anonymous requests (401 from the GHCR token endpoint, 200 for a known public image). GitHub's docs say public packages in this registry can be pulled anonymously, and that a public package can't be made private again.
- **Limit of this evidence.** The old organization path couldn't be read: the registry refuses anonymous requests, and organization SSO blocks the local `gh` token. So it's unknown whether anything is published there, or whether any deployment pulls from it.

## Goals / Non-Goals

**Goals:**
- A default chart install pulls the image this repo released, with no credentials and no overrides.
- No file in the repo, apart from the archived history, still sends a reader to the old path or the old pull secret.
- Only `Build-Docker` writes to the release path.

**Non-Goals:**
- **Deriving the default from the repo owner.** See D1.
- **Changing published charts.** They are never replaced.

## Decisions

Each decision is marked *agreed* (decided with the user) or *planning default* (proposed, open to change at review).

### D1. The default is a literal value, with no CI comparison (planning default)

`image.repository` holds `ghcr.io/mchudinov/example-net-docker` as plain text.

**Alternatives considered:**
- **A CI check comparing the default with the release path.** About 4 lines in `Build-Helm`, but it would only catch another repo move, which is rare. Add it later if the repo moves again.
- **Having the release write the path into `values.yaml`, as it does for `Chart.yaml`.** This would add a second bot-edited file and leave the path on `main` out of date until the next release.

### D2. Packages are public and the chart has no default pull secret (agreed)

The user chose public. The repo is already public and CI builds the images from that source, so this exposes nothing new. Clusters need no secret to create or rotate.

`imagePullSecrets` stays in `values.yaml` as an empty list, so deployers can still set it and mirrored or private copies keep working.

**Alternative considered:** keep the packages private. Every cluster would then need a token that can read a personal account's packages, and the Cloud&DevOps team may not be able to provide one.

### D3. The owner changes visibility, before the next `v*` tag (planning default)

This is a one-way setting on two packages, so it stays with the owner and isn't scripted. It comes first in the tasks so the anonymous checks can run straight away, but only the release depends on it. See Migration Plan.

### D4. `build-docker.sh` is deleted, not corrected (planning default)

The script was last changed in 2023, and it pushes a fixed `0.1.0` tag with a hand-built image. Nothing references it, and the README already documents local `docker build`. Correcting the path would make the risk worse: a push could then land in the real release path as an unsigned, amd64-only image. It stays available in git history.

### D5. `pod-example.yaml` pins `1.2.1` (planning default)

This is the newest release, and `0.2.10` doesn't exist as a tag in this repo. The pin is amd64-only, and nothing updates it at release. It will go stale as the old one did, which is acceptable for a README example.

**Alternative considered:** the floating `1.2` tag. It has the same limits and is less clear about which image the example runs.

### D6. No branch or pull request (agreed)

The work goes directly on `main`, as this repo does. Pushing is left to the user.

## Risks / Trade-offs

- **[A release ships before the packages are public]** The new chart has no pull secret, so its pods can't pull a private image, and the chart itself can't be pulled anonymously. → Task 1 comes first, and Migration Plan states the order. Nothing enforces it automatically.
- **[Making the packages public can't be undone]** Every existing version and signature becomes readable, not just new ones. → They're all built from this public repo, and CI-built images contain no local settings files.
- **[Upgrades that rely on the old default change where they pull from]** It's unknown whether any exist (see Context). → Marked **BREAKING** in the proposal. Such a deployment can set `image.repository` back.
- **[Chart 1.2.1 stays wrong]** Until the next tag, the README's install command gets a chart that defaults to the old path. → The README says to set `image.repository` for 1.2.1 and earlier.
- **[The two paths can drift again]** A repo move or a fork changes the release path but not the default, and nothing reports it. → Accepted (D1). `CLAUDE.md` states the rule.

## Migration Plan

1. The owner makes both packages public.
2. The repo changes land on `main`. `Build-Helm` checks the chart and publishes nothing.
3. The next `v*` tag publishes a chart with the new defaults, along with the first multi-platform image.

**Effect on the archived `add-arm64-image` change:**
- Its task 6.1 can be checked without the `read:packages` login once step 1 is done.
- Its task 6.2 note says the deployment needs `image.repository` overridden. From step 3 onward, it doesn't.

**Rollback:**
- **Repo changes:** revert the commit.
- **Published chart:** published versions aren't replaced, so a bad chart is corrected by the next patch tag.
- **Visibility:** this can't be rolled back.
