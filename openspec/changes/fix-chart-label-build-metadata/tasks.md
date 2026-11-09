All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main` (design D10). Ask the user before each push and before the tag.

Run commands from the repo root in Git Bash unless a task says otherwise. In these tasks:
- `<scratchpad>` is the session's scratchpad directory.
- `<check>` is `bash .github/scripts/check-chart-labels.sh`.
- `<version>` is the current `version` in `helm/Chart.yaml`.
- Set A and set B are the value sets in design D3.

## 1. Build the check and see it fail on today's chart

- [x] 1.1 Create `.github/scripts/check-chart-labels.sh` as described in design D4. Verify that `bash -n` accepts it.
- [x] 1.2 Before touching any template, run `helm package helm --version "<version>+662a06b343e7" --destination <scratchpad>/red`, and confirm the file name contains `+`. Run `<check>` on that package with set A, then set B. Verify that both fail, set A names `serviceaccount.yaml` and `service.yaml`, set B also names `service-preview.yaml`, and no other template or label is reported.

  *Result (2026-11-09):* the package is `example-net-helm-1.3.0+662a06b343e7.tgz`. Set A exits 1 with 2 of 34 label values invalid (`serviceaccount.yaml`, `service.yaml`). Set B exits 1 with 3 of 40 invalid (the same two plus `service-preview.yaml`). Every report is the `chart` label with value `example-net-helm-1.3.0+662a06b343e7`.
- [x] 1.3 Verify the check can't pass with nothing to check (spec: "Check cannot pass with nothing to check"): `<check>` fails on a package built with the plain `<version>`, and fails on a throwaway chart in `<scratchpad>` holding one unlabelled ConfigMap and packaged with a `+` version.

  *Result:* both exit 1, with "has no build metadata" and "contains no helm.sh/chart label" respectively.

## 2. Fix the templates

- [x] 2.1 Delete the `chart:` line from `helm/templates/service.yaml:7`, `helm/templates/serviceaccount.yaml:7` and `helm/templates/service-preview.yaml:8`. Verify that `helm lint . --strict` passes from `helm/`, and that `git diff --stat` shows one line removed from each of the three files.

  *Result (2026-11-09):* `1 chart(s) linted, 0 chart(s) failed`, and the diff is 3 files, 3 deletions.
- [x] 2.2 Package the fixed chart at `<version>+662a06b343e7` into `<scratchpad>/green`. Verify that `<check>` passes four times: with set A, with set B, with no arguments, and with `--set rollout.enabled=true --set rollout.strategy=bluegreen` (spec: "Default values render valid labels", "Blue-green Rollout renders valid labels", "Valid chart passes").

  *Result:* all four exit 0, with 32, 37, 22 and 27 label values valid.
- [x] 2.3 Render that package with set A and set B. Verify that `helm.sh/chart` reads `example-net-helm-<version>_662a06b343e7` on the same 6 and 7 objects as the baseline in design Context, that no object has a label with the key `chart`, and that no label value contains `+` (spec: "Chart version label uses the replaced form", "Every labelled object keeps the chart label", "No separate chart label").

  *Result:* `example-net-helm-1.3.0_662a06b343e7` on the same 6 and 7 objects, no `chart` label, and `+662a06b343e7` appears on no rendered line.
- [x] 2.4 Verify that `grep -rn '\.Chart\.Version' helm/templates` lists only `_helpers.tpl:32` and `NOTES.txt:2`.

  *Result:* those two lines only.

## 3. Prove the check catches a reintroduced label

- [x] 3.1 For each of the nine templates that set labels (design Context), copy the fixed chart into `<scratchpad>` and add `chart: "{{ .Chart.Name }}-{{ .Chart.Version }}"` as the first entry of that template's metadata `labels:` block. Package each copy at `<version>+662a06b343e7` and run `<check>` with set A and set B. Verify that, for all nine, at least one set fails and names that template and the `chart` label (spec: "Raw version in a default template fails the check", "Raw version in a conditional template fails the check"). Record that this is local evidence and that the failing case wasn't run on live CI (design Risks).

  *Result (2026-11-09, local evidence, not run on live CI):* all nine fail and name the template and the `chart` label. `serviceaccount.yaml`, `service.yaml`, `hpa.yaml` and `tests/test-connection.yaml` fail in both sets. `deployment.yaml` and `ingress.yaml` fail in set A only. `service-preview.yaml`, `rollout-bluegreen.yaml` and `ingress-preview.yaml` fail in set B only.
- [x] 3.2 In a `<scratchpad>` copy of the fixed chart, remove `| replace "+" "_"` from `app.chart`, then package and run `<check>`. Verify that it fails on `helm.sh/chart`.

  *Result:* exit 1, reporting `helm.sh/chart` on all 6 objects in set A.

## 4. Add the checks to `Build-Helm`

- [x] 4.1 In `.github/workflows/build-helm.yaml`, add `.github/workflows/build-helm.yaml` and `.github/scripts/check-chart-labels.sh` to the `paths` of both `push` and `pull_request` (design D8). Verify by reading the two lists (spec: "Change to the checks runs them"). The push in task 6.2 also changes `helm/**`, so it doesn't exercise these paths alone, and the record should say so.

  *Result (2026-11-09):* both lists read `helm/**`, the workflow and the script. Verified by reading only. A push that changes only these two paths has not been run.
- [x] 4.2 In `Check-Helm`, before the `Package helm` step, add a step that packages the chart at `<CHART_VERSION>+662a06b343e7` into `$RUNNER_TEMP` with `--version` only (design D6, D7), and a step that runs the check on that package with set A and set B (design D3). Verify by reading the diff that the existing steps are unchanged. Task 6.2 proves they run.
- [x] 4.3 After the `Test install helm chart` step, add a step that installs the `+` package with `--generate-name` in its own namespace, using `--create-namespace` (design D5). Verify by reading the diff that it doesn't use `default`. Task 6.2 proves it runs (spec: "Cluster accepts the chart").

  *Result:* the step uses `--namespace build-metadata`. A local `--dry-run=client` of the `+` package generates the release name `example-net-helm-1-<timestamp>`, so no `+` reaches `app.kubernetes.io/instance`.

## 5. Docs

- [x] 5.1 Update the Build-Helm section of `CLAUDE.md`: add the two new trigger paths to line 135, add the label check and the `+` install to the "Every run checks the chart" list, and state that a new flag guarding a template with labels must be added to the value sets. Verify by reading the section.

## 6. Validate and check in CI

- [x] 6.1 Verify that `openspec validate fix-chart-label-build-metadata --strict` passes.

  *Result (2026-11-09):* `Change 'fix-chart-label-build-metadata' is valid`.
- [ ] 6.2 Commit on `main`, and confirm that `git ls-files --eol .github/scripts/check-chart-labels.sh` shows `i/lf`, because a script committed with CRLF fails on the Linux runner. Ask the user, and push. Verify with `gh run list --commit <sha>` and the run's jobs that `Build-Helm` succeeded including the new steps, that `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped, and that `Build-Docker` didn't run. This is the k3d install's first real run. If the run fails, stop and don't tag.

## 7. Release 1.3.1

- [ ] 7.1 Once task 6.2 is green, ask the user, then tag `v1.3.1` on that commit and push only the tag (design D9). Verify that `git ls-remote --tags origin v1.3.1` points at the commit from task 6.2.
- [ ] 7.2 Verify that the `Build-Docker` run for `v1.3.1` and the `Build-Helm` run that follows it both succeeded, including publishing and signing, and that `helm/Chart.yaml` on `origin/main` shows `version` and `appVersion` as `1.3.1` from the bot's commit.
- [ ] 7.3 Verify that `docker buildx imagetools inspect ghcr.io/mchudinov/example-net-docker:1.3.1` lists `linux/amd64` and `linux/arm64`.
- [ ] 7.4 Verify the released chart with no credentials: `HELM_REGISTRY_CONFIG=<scratchpad>/no-credentials.json helm pull oci://ghcr.io/mchudinov/example-net-helm --version 1.3.1 -d <scratchpad>/chart-pull` succeeds, `helm show chart` on the pulled package reports `version` and `appVersion` as `1.3.1`, and its default and blue-green renders have no label with the key `chart`.
- [ ] 7.5 Tell the user that the Flux source has to point at chart 1.3.1 (design Migration Plan). Verify by recording here that the Flux install wasn't tested from this repo, along with the result if the user reports one.
