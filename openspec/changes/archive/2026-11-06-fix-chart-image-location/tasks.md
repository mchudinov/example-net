All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main` (design D6). Pushing and tagging are left to the user.

Task 1.1 is the owner's, because the setting is one-way (design D3). If it isn't done yet, carry on with sections 2 to 4 and leave the tasks that depend on it open: 1.2, the resolve check in 3.1, and section 6.

`<scratchpad>` is the session's scratchpad directory.

## 1. Make the packages public (design D2, D3)

- [x] 1.1 **Owner:** in each package's settings, under "Danger Zone", change the visibility to public: `https://github.com/users/mchudinov/packages/container/example-net-docker/settings` and `https://github.com/users/mchudinov/packages/container/example-net-helm/settings`. GitHub's docs say this can't be undone. Verify with task 1.2.
- [x] 1.2 Verify access with no credentials (spec: "Image is readable without credentials", "Chart is readable without credentials"):
  - Get an anonymous token with `curl` from `https://ghcr.io/token?scope=repository:mchudinov/example-net-docker:pull&service=ghcr.io`. It returns 200, not the 401 recorded on 2026-11-06.
  - Use that token to request `https://ghcr.io/v2/mchudinov/example-net-docker/manifests/1.2.1`, accepting the OCI index and manifest media types. It returns 200.
  - `HELM_REGISTRY_CONFIG=<scratchpad>/no-credentials.json helm pull oci://ghcr.io/mchudinov/example-net-helm --version 1.2.1 -d <scratchpad>/chart-pull` succeeds.

  *Result (2026-11-06, not passing):* the owner reported both packages public, but the token endpoint still returned 401 for both at 08:07 UTC and at 11:20 UTC, about 3 hours 15 minutes later. A known public image returned 200 on the same endpoint at 08:06. At 08:06, the manifest request returned 403 and `helm pull` failed with 401. Both package pages returned 404 when logged out. The repository was already public, but that setting doesn't carry over: GitHub's docs say a linked package inherits access permissions "but not the visibility" of its repository.

  *Result (2026-11-06, passing, after the owner changed each package's own setting):* at 12:47 UTC the token endpoint returned 200 for both packages, the `1.2.1` manifest request returned 200, and `helm pull` succeeded with digest `sha256:c981e4b9…`. Both package pages returned 200 when logged out.

  The `1.2.1` manifest could be read directly for the first time. It's an OCI index with one `linux/amd64` image and one attestation manifest, which confirms that 1.2.1 is amd64-only. The published chart 1.2.1 still has the old `image.repository` and pull secret, as expected.

## 2. Chart defaults (design D1, D2)

- [x] 2.1 In `helm/values.yaml`, set `image.repository` to `ghcr.io/mchudinov/example-net-docker` and `imagePullSecrets` to `[]`. Don't change the templates or the `Chart.yaml` version fields. Verify from `helm/` that `helm lint . --strict` passes.

  *Result (2026-11-06):* `1 chart(s) linted, 0 chart(s) failed`. Only `helm/values.yaml` changed under `helm/`.
- [x] 2.2 Verify the renders from `helm/`, with `<app version>` being `appVersion` in `Chart.yaml` (spec: all scenarios under "Chart's default image is the image releases publish", and "Default render references no pull secret", "Deployer-supplied pull secret is used"):
  - `helm template t .` and `helm template t . --set rollout.enabled=true`: each shows `image: "ghcr.io/mchudinov/example-net-docker:<app version>"` and has no `imagePullSecrets`.
  - Both of those with `--set-json 'imagePullSecrets=[{"name":"my-secret"}]'` added: each references `my-secret`.
  - `helm template t . --set image.repository=example.test/app --set image.tag=9.9.9`: shows `image: "example.test/app:9.9.9"`.

  *Result:* all as expected, with app version `1.2.1`. The default and Rollout renders both show `ghcr.io/mchudinov/example-net-docker:1.2.1` and no `imagePullSecrets`. With the secret set, both reference `my-secret`. The override renders `example.test/app:9.9.9`.

## 3. Example manifest and script (design D4, D5)

- [x] 3.1 In `pod-example.yaml`, set the image to `ghcr.io/mchudinov/example-net-docker:1.2.1` and remove the `imagePullSecrets` block. Verify:
  - `grep -c imagePullSecrets pod-example.yaml` returns `0`.
  - `kubectl apply --dry-run=client --validate=false -f pod-example.yaml` accepts the file and creates nothing. If `kubectl` can't work without a cluster, record that and check the file by reading it.
  - After task 1.1, the check in task 1.2 passes for this exact tag, so the example's image resolves.

  *Result (2026-11-06):* the file is edited. The grep returns `0`, and `kubectl` reports `pod/example-net-test created (dry run)`, with nothing created. The resolve check passed once task 1.1 was done: the file's exact image, `ghcr.io/mchudinov/example-net-docker:1.2.1`, returned 200 with no credentials (task 1.2).
- [x] 3.2 Delete `build-docker.sh`. Verify that `grep -rnI 'build-docker\.sh' . --exclude-dir=.git --exclude-dir=archive --exclude-dir=fix-chart-image-location` finds nothing.

  *Result:* deleted, and the grep finds nothing. The last version is in git history at `113fe57`.

## 4. Docs

- [x] 4.1 Update `README.md`:
  - line 83: the image and chart are pushed under the *mchudinov* account, and both are public;
  - lines 189 and 195: remove the pull secret prerequisite and the request to the Cloud&DevOps team. Leave lines 191-193;
  - line 211: replace the GHCR login step with a note that chart versions 1.2.1 and earlier default to an old image location, so installing one needs `--set image.repository=ghcr.io/mchudinov/example-net-docker`;
  - line 214: install from `oci://ghcr.io/mchudinov/example-net-helm`.

  Verify by reading the rendered sections "GitHub actions", "Prerequisites" and "Install the remote Helm Chart".

  *Result (2026-11-06):* all three sections read correctly. The sentence "Both packages are public" became true when task 1.1 was done.
- [x] 4.2 Update `CLAUDE.md`, in the Helm chart section:
  - **Cluster prerequisites:** drop the pull secret and keep the priority class;
  - add a **Default image** bullet: `image.repository` must be `ghcr.io/mchudinov/example-net-docker`, the path Build-Docker pushes to. The workflows derive that path from the repo owner while the chart hard-codes it, and CI doesn't compare them, so update the default if the repo moves. Both packages are public, so the chart has no default pull secret.

  Verify by reading the section.

  *Result:* the section has both bullets. As in task 4.1, the sentence about public packages became true when task 1.1 was done.
- [x] 4.3 Verify nothing still points at the old location or secret (design goal): `grep -rnIiE 'wss-ea-cloud|container-registry-pullsecret' . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=TestResults --exclude-dir=graphify-out --exclude-dir=archive --exclude-dir=fix-chart-image-location` finds nothing.

  *Result:* the grep finds nothing.

## 5. Validate and check in CI

- [x] 5.1 Verify `openspec validate fix-chart-image-location --strict` passes.

  *Result (2026-11-06):* `Change 'fix-chart-image-location' is valid`.
- [x] 5.2 After the user pushes, verify with `gh run list --commit <sha>` and the run's jobs:
  - `Build-Helm` succeeded, including `helm lint`, the test install and the Polaris audit;
  - its publish jobs were skipped, so no chart was published;
  - `Build-Docker` didn't run.

  *Result (2026-11-06):* commit `21536ea`, run 42184955509. It was the only run for that commit and it succeeded, including `Lint helm`, `Test install helm chart` and `Use Polaris`. `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped. The latest `Build-Docker` run is still the v1.2.1 release (42004779668).

## 6. After the next `v*` tag

- [x] 6.1 Confirm task 1.1 is done before the tag is pushed (design Risks).

  *Result (2026-11-06):* task 1.2 passed at 12:47 UTC, and the newest tag on the remote was still `v1.2.1`, so no release has shipped with the new defaults while the packages were private.
- [x] 6.2 Verify the released chart with no credentials, using the `HELM_REGISTRY_CONFIG` form from task 1.2:
  - `helm show values oci://ghcr.io/mchudinov/example-net-helm --version <next>` shows the new `image.repository` and `imagePullSecrets: []`;
  - `helm template t oci://ghcr.io/mchudinov/example-net-helm --version <next>` shows `ghcr.io/mchudinov/example-net-docker:<next>` and no `imagePullSecrets`;
  - the manifest check from task 1.2 returns 200 for tag `<next>`.

  *Result (2026-11-06):* release `v1.3.0`, `Build-Docker` run 42220698024 and `Build-Helm` run 42220906429, both successful with chart publishing and signing included. With no credentials, the released chart's values show `repository: ghcr.io/mchudinov/example-net-docker` and `imagePullSecrets: []`. Its Deployment and Rollout renders both show `ghcr.io/mchudinov/example-net-docker:1.3.0` and no `imagePullSecrets`. The `1.3.0` manifest request returned 200. No pod was started, so this shows the chart's image reference resolves, not that a pod runs.
