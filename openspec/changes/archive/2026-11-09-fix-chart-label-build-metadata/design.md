## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-09:

- **Where the version reaches a label.** `.Chart.Version` appears in `helm/templates/service.yaml:7`, `serviceaccount.yaml:7` and `service-preview.yaml:8` (the raw `chart:` label), in the `app.chart` helper at `_helpers.tpl:32`, and in `NOTES.txt:2`, which is install text and not a label.
- **What Flux changes.** The helm-controller docs give the format `<version>+<digest[0:12]>` and show an event with `revision: 6.6.1+0cc9a8446c95` next to a plain `app-version: 6.6.1`. So `appVersion`, and through it the image tag and `app.kubernetes.io/version`, keep no `+`.
- **Which templates a default install reaches.** Nine templates set labels. A default install sends three to the API server: `serviceaccount.yaml`, `service.yaml` and `deployment.yaml`. The others need non-default values (`hpa.yaml`, `ingress.yaml`, `service-preview.yaml`, `rollout-bluegreen.yaml`, `ingress-preview.yaml`) or are a `helm test` hook (`tests/test-connection.yaml`). `rollout-bluegreen.yaml` can't be installed on k3d at all, because the cluster has no Argo Rollouts CRDs.
- **Object names.** Every object is named from `app.name`, which is the chart name and not the release name. `app.fullname` is defined but unused. The existing `Test install helm chart` step has already created these objects in the `default` namespace when a later install would run.
- **Version flow.** `github-actions[bot]` wrote all six version commits to `helm/Chart.yaml`. `Update-Chart-Version` sets both fields from the tag. Its commit action (`planetscale/ghcommit-action@v0.1.33`) exits 0 on an empty diff.
- **Release state.** Tag `v1.3.1` is free, and neither image nor chart 1.3.1 exists. Image 1.3.0 lists `linux/amd64` and `linux/arm64`.
- **Baseline of `helm.sh/chart`, at 1.3.0.** It's on 6 objects with ingress and autoscaling on (ServiceAccount, Service, Deployment, HorizontalPodAutoscaler, Ingress, test Pod). It's on 7 with blue-green Rollout, preview ingress and autoscaling on (ServiceAccount, Service, preview Service, HorizontalPodAutoscaler, preview Ingress, Rollout, test Pod).
- **Local tools.** `helm` 4.1.4, Docker, Git Bash and GNU awk are available. `k3d` and `yq` aren't. A line-based `awk` parser read 34 and 40 label entries from those two renders, and found exactly 6 and 7 `helm.sh/chart` labels.
- **Limit of this evidence.** No render with a `+` version has been run yet, because planning stayed read-only. Task 1 does that first. Flux itself can't be tested from this repo.

## Goals / Non-Goals

**Goals:**
- A chart version containing `+` can't reach a label, on any path the chart can render.
- CI fails if that stops being true, including in templates a default install never creates.
- The check gives the same result locally and in CI, so a deliberately broken chart can be tested without pushing it.

**Non-Goals:**
- **Testing Flux in CI.** The check reproduces the version form Flux produces. It doesn't run Flux.
- **Checking anything other than label values.** Label keys, annotations and object names are unchanged by this bug.

## Decisions

Each decision is marked *agreed* (decided with the user) or *planning default* (proposed, open to change at review).

### D1. Delete the raw label, rather than escaping it (agreed)

The version then reaches a label only through `app.chart`, the same path EuGo-docint's chart uses.

**Alternative considered:** add `| replace "+" "_"` to the three labels. They would then duplicate `helm.sh/chart` with the same value and a second copy of the escaping.

### D2. A render check guards every template, next to the k3d install (planning default)

The check packages the chart at a `+` version, renders it, and tests every label value against the Kubernetes rule. It needs no cluster, so it covers the Rollout without CRDs, and the `helm test` hook too. It tests the outcome rather than a spelling, so a raw value that arrives through a new helper or a variable is also caught.

The k3d install stays as the proof that a real API server accepts the chart on the default path.

**Alternatives considered:**
- **The k3d install alone.** It reaches 3 of the 9 templates, so `service-preview.yaml`, one of the three with this bug, could regress unnoticed.
- **A grep for `.Chart.Version` outside the helper and `NOTES.txt`.** One line, and it covers future templates. It only catches that exact spelling, and it would flag a harmless use in an annotation.
- **Installing Argo Rollouts CRDs on k3d.** It adds a download and still doesn't reach the `helm test` hook.

### D3. Two value sets in CI, and the script renders one set per call (planning default)

- **Set A:** `--set ingress.enabled=true --set autoscaling.enabled=true`
- **Set B:** `--set rollout.enabled=true --set rollout.strategy=bluegreen --set rollout.ingress.enabled=true --set autoscaling.enabled=true`

Together they render all nine templates. `rollout-canary.yaml` is an empty stub, and `removed-values.yaml` renders no manifest.

The script takes the package and then any `helm template` arguments, and the workflow calls it once per set. That keeps the sets visible in the workflow, and lets tasks run the exact default and blue-green cases from the spec by passing different arguments.

### D4. A bash and awk script at `.github/scripts/check-chart-labels.sh` (planning default)

The script:
- reads the version with `helm show chart` and fails if it has no `+`;
- renders with `set -euo pipefail`, so a failed render fails the check;
- removes carriage returns, because templates can be stored with CRLF on a Windows checkout;
- walks each `labels:` block by indentation and tests each value against `^[A-Za-z0-9]([-A-Za-z0-9_.]*[A-Za-z0-9])?$`, plus the empty value and a 63-character limit, using no `{n}` regex intervals, because the runner's default `awk` is `mawk`;
- reports each invalid value as a `::error::` line naming the source template, the key and the value;
- fails if the render contained no `helm.sh/chart` label, so it can't pass with nothing to check.

The workflow runs it with `bash <path>`, which avoids depending on a file mode set from a Windows checkout.

**Alternatives considered:**
- **Inline in the workflow.** It couldn't be run locally against a broken chart, which is how the "fails if reintroduced" criterion gets its evidence.
- **`yq`.** It's a real YAML parser, but it isn't installed locally, so the local and CI runs would differ. The chart's output is plain block-style YAML, so line-based parsing is enough.
- **Under `helm/`.** It would be packaged into the chart unless `.helmignore` also listed it.

### D5. The `+` install gets its own namespace (planning default)

The step uses `helm install <package> --generate-name --namespace <name> --create-namespace`, because a second install in `default` should fail on Helm's ownership check, whatever the labels say (see Context, Object names).

**Alternatives considered:**
- **Replacing the existing test install.** It would stop proving that the chart installs at the version that actually gets published.
- **Switching templates to `app.fullname`.** It removes the collision but renames every object, which the user scoped out.

### D6. The test version is `<Chart.yaml version>+662a06b343e7`, set with `--version` only (planning default)

It has the shape Flux produces, and following `Chart.yaml` keeps it a valid version as releases move on. `--app-version` isn't passed, because Flux doesn't change `appVersion` and a `+` there would test something that doesn't happen.

### D7. The `+` package is built before the existing `Package helm` step, into `$RUNNER_TEMP` (planning default)

`.helmignore` doesn't exclude `*.tgz`, so a package built after `Package helm` would contain that earlier archive. Writing to `$RUNNER_TEMP` keeps `helm/` as it is for the steps that follow.

### D8. `Build-Helm` also triggers on its own workflow and script (planning default)

`.github/workflows/build-helm.yaml` and `.github/scripts/check-chart-labels.sh` join `helm/**` in both the `push` and `pull_request` paths, as `Build-Dotnet` and `Check-Docker` already do for their own files. Without this, a broken check would first run during a release, after the image is already published.

### D9. The version comes from the tag only (planning default)

The brief asked to bump both fields in `helm/Chart.yaml`. This departs from that wording and reaches the same result: `CLAUDE.md` says not to hand-edit those fields, and the tag sets both. A hand-edit wouldn't break the release (see Context, Version flow), but it adds nothing.

### D10. No branch or pull request (agreed)

The work goes directly on `main`, as this repo does. The user is asked before each push and before the tag.

## Risks / Trade-offs

- **[The new check first fails during a release]** The image would already be out, with no chart, and the fix would be another patch tag. → The change lands on `main` first, and the tag waits for a green `Build-Helm` (see Migration Plan).
- **[A future template behind a new flag isn't in either value set]** The render check wouldn't see it. → `CLAUDE.md` says to extend the value sets when a new flag guards a template with labels. The k3d install doesn't help here.
- **[Line-based parsing misses flow-style labels]** `labels: {a: b}` wouldn't be read. → Accepted. Nothing in the chart writes labels that way.
- **[The negative case can't be shown on live CI]** That would take a broken commit on `main`. → Tasks prove it locally with the same script, red before the fix and on deliberately broken copies, and record that it wasn't run live.
- **[The k3d step can't be run locally]** `k3d` isn't installed here. → Its first run is the push to `main`, which publishes nothing.
- **[Chart 1.3.0 stays broken under Flux]** Published charts aren't replaced. → Flux has to move to 1.3.1.
- **[Flux hits a different problem next]** The chart needs priority class `user-normal`. Without it the objects are accepted but pods can't start, and the CI install doesn't wait for pods. → Not caused by `+`, and already listed as a cluster prerequisite in `CLAUDE.md`.

## Migration Plan

1. The repo changes land on `main`. `Build-Helm` checks the chart and publishes nothing.
2. Once that run is green, tag `v1.3.1` on a commit containing the fix. `Build-Docker` tests, pushes and signs the image, and the bot sets `Chart.yaml` to 1.3.1. `Build-Helm` then publishes and signs chart 1.3.1 from `main`.
3. Whoever runs the cluster points the Flux source at chart 1.3.1. The docs say a new chart version resets a stalled HelmRelease's failure counters. If it doesn't recover, `flux reconcile helmrelease <name> --reset` does.

**Rollback:**
- **Repo changes:** revert the commit.
- **Published chart or image:** versions aren't replaced, so a bad release is corrected by the next patch tag.
