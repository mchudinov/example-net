## 1. App and chart change (dotnet-developer agent, one commit on `main`; design D1 to D3 and D5)

Give 1.1 to 1.3 to the dotnet-developer agent in one brief. It works directly on `main`: no `agent/*` branch, no push, no PR.

- [x] 1.1 Before any edits, save baselines so the result can be compared:
  - from `helm/`, run `helm template t . > <scratchpad>/render-before.yaml`;
  - record the resolved package count of `ExampleNet/obj/project.assets.json` (46 today).

  Then make the app edits:
  - `ExampleNet/Program.cs`: delete `services.AddElasticApm();`.
  - `ExampleNet/ExampleNet.csproj`: delete the `Elastic.Apm.NetCoreAll`, `Elastic.Apm.SerilogEnricher`, `Elastic.Serilog.Sinks` and `Serilog.Sinks.Elasticsearch` references.
  - `ExampleNet/appsettings.json`: delete the `ElasticApm` section.

  Verify:
  - `git grep -niE 'elastic|apm' -- ExampleNet` returns nothing.
  - `dotnet restore`, `dotnet build -c Release` and `dotnet test` all pass.
  - `dotnet list ExampleNet/ExampleNet.csproj package --include-transitive` lists no `Elastic*`, `Elasticsearch*` or `MongoDB*` package. The resolved count is 13, and the remaining packages keep their versions.
  - `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for either project.

  *Observed (2026-11-03):*
  - The `git grep` is empty.
  - Build: 0 errors. The one warning is the existing `CS8605` in `TimedHostedService.cs`.
  - Tests: 1 passed, 0 failed.
  - Resolved packages went from 46 to 13. All 13 match `packages-before.txt` exactly, so no version changed.
  - The vulnerability audit is clean for `ExampleNet` and `ExampleNetTests`.
- [x] 1.2 Make the chart edits:
  - `helm/values.yaml`: delete the `elastic:` and `keyvault:` blocks.
  - `helm/templates/deployment.yaml`: delete the `ElasticApm__*`, `Serilog__WriteTo__1__*` and Key Vault secret env blocks. Delete the `volumeMounts:` and `volumes:` keys themselves, which held only the CSI entry.
  - Delete `helm/templates/secretprovider.yaml`.
  - Add `helm/templates/removed-values.yaml` (design D3). It renders no manifest. It calls `fail` with a message naming the removed values when `elastic.log.enabled`, `elastic.apm.enabled` or `keyvault.enabled` is true. Read the flags with `dig ... false .Values.AsMap`. Add a one-line comment saying the file can go once deployers have migrated.

  Verify from `helm/`:
  - `helm lint . --strict` passes.
  - `helm template t .` succeeds. Its diff against `render-before.yaml` shows only removed whitespace or blank lines and the empty `volumes:` key.
  - `helm template t . --set elastic.log.enabled=false --set elastic.apm.enabled=false --set keyvault.enabled=false` succeeds.
  - The default render and the explicit-`false` render both contain no `ElasticApm__`, no `Serilog__WriteTo__1__`, no `SecretProviderClass` and no `secrets-store`. This covers the spec scenario "Chart renders no Elastic or Key Vault configuration".
  - Each of `--set elastic.log.enabled=true`, `--set elastic.apm.enabled=true` and `--set keyvault.enabled=true` makes `helm template t .` exit non-zero with the guard message. Each also fails when combined with `--set rollout.enabled=true`. This covers "Turning on a removed chart feature fails rendering".
  - Use `helm template` for these checks, not `helm lint`: Helm 4's lint logs `fail` at INFO level and still passes (design, Context).
  - `git grep -niE 'elastic|keyvault|secrets-store|secretprovider' -- helm` matches only `removed-values.yaml`.

  *Observed (2026-11-03):*
  - `helm lint . --strict` passed. Its only note is the existing `icon is recommended`.
  - Ignoring line endings (`diff --strip-trailing-cr`), the default-render diff is removals only: 4 whitespace-only lines and `volumes:`.
  - A plain `diff` also shows `value: "Information"` as changed. That line lost its `\r`, because it is now the last line of the manifest and Helm trims trailing whitespace. This is a CRLF-checkout artifact; an LF-only render shows only removals.
  - The explicit-`false` render is byte-identical to the default render. Neither contains `ElasticApm__`, `Serilog__WriteTo__1__`, `SecretProviderClass` or `secrets-store`.
  - All 6 guard runs (each flag, with and without `rollout.enabled=true`) exited 1 with the guard message.
- [x] 1.3 Commit 1.1 and 1.2 together on `main`. Verify `git show --stat HEAD` lists exactly these seven files:
  - `ExampleNet/Program.cs`
  - `ExampleNet/ExampleNet.csproj`
  - `ExampleNet/appsettings.json`
  - `helm/values.yaml`
  - `helm/templates/deployment.yaml`
  - `helm/templates/secretprovider.yaml` (deleted)
  - `helm/templates/removed-values.yaml` (new)

  Don't push yet.

  *Observed (2026-11-03):* commit `8aef5af` on `main`, not pushed. It changes exactly the 7 paths: 5 insertions, 125 deletions.

## 2. Runtime checks (main session, against the built container)

If Docker isn't running, use the fallback in 2.4.

- [x] 2.1 Build the image with `docker build -t example-net-docker:no-elastic .`. Export its filesystem with `docker create` and `docker export | tar -t`. Verify that nothing under `App/` matches `Elastic|Elasticsearch|MongoDB|SharpCompress|Snappier|ZstdSharp|DnsClient`.

  *Observed (2026-11-03):* built from `8aef5af` on Docker 29.9.2. `App/` holds 22 entries, and none match.
- [x] 2.2 Start a listener on a user-defined Docker network. Any minimal HTTP server that logs requests will do, for example `python -m http.server` in a `python:3-alpine` container.

  Run the image on the same network:
  - as `--user 1000:1000` with `--read-only`;
  - with port 50001 published;
  - with these variables, all pointing at the listener:
    - `ElasticApm__Enabled=true`
    - `ElasticApm__ServerUrls=http://<listener>:8200`
    - `Serilog__WriteTo__1__Name=Elasticsearch`
    - `Serilog__WriteTo__1__Args__nodeUris=http://<listener>:8200`
    - `Serilog__Enrich__2=WithElasticApmCorrelationInfo`

  Verify:
  - `GET /`, `/healthz`, `/readyz` and `/livez` on the published port each return 200, and `/` returns `Ok`.
  - The container's stderr shows `Unable to find a method called Elasticsearch` and `Unable to find a method called WithElasticApmCorrelationInfo`.
  - After at least 60 s, the listener has logged no requests.

  This covers the scenarios "Monitoring variables have no effect" and "Leftover Elastic logging configuration does not stop the service", and re-checks the endpoint and restricted-security-context requirements.

  *Observed (2026-11-03):*
  - The listener was `python:3-alpine` running `http.server` on port 8200. A positive-control request from another container (`GET /control-probe`) appeared in its log, which proves it records traffic.
  - The app ran as `1000:1000` with `readonly=true`. `/healthz` returned 200 about 1 s after start. `/`, `/healthz`, `/readyz` and `/livez` all returned 200, and `/` returned `Ok`.
  - stderr showed `Unable to find a method called WithElasticApmCorrelationInfo` and `Unable to find a method called Elasticsearch`.
  - After 65 s the app was still running. The listener's log held only the control probe.
- [x] 2.3 Run the image twice more and check the logs:
  - with `Settings__TimeSpan=1`: periodic entries appear about once per second;
  - with `Serilog__MinimumLevel=Warning`: there are no periodic entries below Warning.

  This covers "Periodic log interval is configurable" and "Minimum log level is configurable".

  *Observed (2026-11-03):*
  - `TimeSpan=1`: entries reached Count=12 at Uptime=11.01 s, which is 1.00 s per entry.
  - `MinimumLevel=Warning` (with `TimeSpan=1`): 15 ticks ran, and the only periodic entries written were 2 Warning, 2 Error and 3 Critical. None was below Warning.
- [x] 2.4 Fallback only if Docker isn't running: run 2.2 and 2.3 with `dotnet run --project ExampleNet` and the same variables, using a local `python -m http.server` as the listener. Record 2.1 and the read-only, non-root part of 2.2 as **not run**, with the error.

  *Not needed (2026-11-03):* Docker was running, so 2.1 to 2.3 ran against the real container.

## 3. Documentation (main session)

- [x] 3.1 In `README.md`, delete:
  - the "Elastic APM" contents entry;
  - `- Elasticsearch` in the logging list;
  - the "Elastic APM" section;
  - the `ElasticApm` blocks, the `Elasticsearch` `WriteTo` entries and `WithElasticApmCorrelationInfo` from both JSON config examples;
  - the `elastic:` and `keyvault:` blocks from the custom `values.yaml` example;
  - the Key Vault "NB!" note.

  Verify:
  - `Select-String -Path README.md -Pattern 'elastic|apm|keyvault|key vault'` returns nothing.
  - Both JSON examples still parse: extract each block and run it through `python -c "import json,sys; json.load(sys.stdin)"`.
  - The `values.yaml` example still parses as YAML.

  *Observed (2026-11-03):*
  - The `Select-String` check (run as `grep -niE`) finds nothing.
  - Both JSON examples parse with Python's `json`.
  - PyYAML isn't installed, so the YAML example was checked differently: it was extracted and passed to `helm template t ./helm -f`. It rendered `Settings__TimeSpan=5` and `Serilog__MinimumLevel=Information`.
  - README diff: 2 insertions, 62 deletions.
- [x] 3.2 In `CLAUDE.md`:
  - Commands: replace the `helm template t . --set elastic.log.enabled=true --set keyvault.enabled=true` line with a plain `helm template t .`.
  - Configuration flow: drop the Elastic APM sentence.
  - Helm chart: drop `ElasticApm__*` from the values-to-env bullet, and delete the "Elasticsearch sink" and "Key Vault" bullets.
  - Helm chart: add one bullet saying that `templates/removed-values.yaml` fails rendering when a removed `elastic.*.enabled` or `keyvault.enabled` flag is true, and can be deleted once deployers have migrated.

  Verify that `Select-String -Path CLAUDE.md -Pattern 'elastic|apm|keyvault|key vault|secretprovider'` matches only the new bullet.

  *Observed (2026-11-03):*
  - The grep matches only line 71, the new "Removed-values guard" bullet.
  - Two extra edits were needed to get there:
    - The Argo Rollouts bullet said "no env vars or Key Vault mounts"; it now says "no env vars".
    - The Configuration-flow line about the APM registration was replaced with the spike finding: Serilog skips a `WriteTo` or `Enrich` entry whose package isn't referenced and writes `Unable to find a method called ...` to stderr.
  - Diff: 7 insertions, 9 deletions.
- [x] 3.3 Commit 3.1, 3.2 and this change's `openspec/changes/remove-elastic-and-keyvault/` files on `main`. Verify that `git show --stat HEAD` lists only `README.md`, `CLAUDE.md` and files under that change directory.

  *Observed (2026-11-03):* commit `457b065` lists exactly `CLAUDE.md`, `README.md` and the 5 files of the change directory.

## 4. Push and CI

- [x] 4.1 **Ask the user before pushing.** Then push both commits. Verify against the pushed SHAs:
  - `Build-Dotnet` runs, because `ExampleNet/**` changed, and succeeds.
  - One `Build-Helm` run with event `push` exists. `gh run view <id> --json jobs` shows `Check-Helm success`, and `Build-and-Push-Helm` and `Sign-Helm-Chart` as `skipped`. `Check-Helm` covers lint, the k3d install and Polaris with a score of at least 90.
  - Nothing is published.

  *Observed (2026-11-03). The user pushed and released:*
  - The user merged rather than rebased. `fb38acc` merges `8aef5af` and `457b065` with the release bot's `d797e16`, which bumped the chart to 1.1.0.
  - `e175bda` adds the "1.1.0 predates this change" wording to the proposal and design.
  - The user tagged `v1.2.0` at `e175bda`.
  - **`v1.1.0` predates this change.** Its tag is at `c2f5226`, so the released 1.1.0 image and chart still contain Elastic and Key Vault.
  - Build-Dotnet `41692729653` (push, `fb38acc`): `Build-and-Test` passed.
  - Build-Helm `41692729560` (push, `fb38acc`): **hung.**
    - Lint passed at 18:20:04.
    - The step "Create a test k3d cluster for dry-run install" (`AbsaOSS/k3d-action`) had not finished 6 minutes later. At 18:26 it was still `in_progress`, and the publish jobs had not been evaluated.
    - The release run's whole `Check-Helm` took 44 s.
  - Build-Docker `41692964753` (tag `v1.2.0`, `e175bda`) passed all four jobs: the test gate, push, sign and the chart-version update.
  - Build-Helm `41693317431` (`workflow_run`, `f994a01`) passed all three jobs: `Check-Helm` (lint, k3d install and Polaris), `Build-and-Push-Helm` and `Sign-Helm-Chart`.
    - `f994a01` is `e175bda` plus the bot's bump to 1.2.0.
    - So chart 1.2.0 was checked, published and signed from the content of this change.
  - Released 1.2.0, checked from git (the GHCR package is private):
    - The image source (`e175bda`) has no `AddElasticApm`.
    - The chart source (`f994a01`) has no `elastic:` or `keyvault:` values and no `secretprovider.yaml`, and it includes `removed-values.yaml`.
    - Rendering it produces 0 `ElasticApm__`, `Serilog__WriteTo__1__`, `SecretProviderClass` or `secrets-store` matches.
    - `--set elastic.apm.enabled=true` fails with the guard message.
  - **Still open:** the push-run check that `Build-and-Push-Helm` and `Sign-Helm-Chart` show as `skipped`. Run `41692729560` has to finish, or be cancelled and re-run.
  - **Accepted as is by the user (2026-11-03), and left open:**
    - Attempt 1 of `41692729560` was cancelled at 18:31:10 (`completed/cancelled`), still in the k3d step.
    - Attempt 2 started at 18:31:12. Its result is not recorded here.
    - The released content passed the full `Check-Helm` in `41693317431`.
    - The behaviour that a push run does not publish was verified in the archived `helm-publish-only-on-release` change, in run `41658988349`.
  - **Closed after archiving:**
    - Attempt 2 of `41692729560` finished `completed/success`. `Check-Helm` (18:31:24 to 18:32:15) passed lint, the k3d cluster, the dry-run and real install, and Polaris.
    - `Build-and-Push-Helm` and `Sign-Helm-Chart` were `skipped`.
    - The attempt-1 hang was transient, in `AbsaOSS/k3d-action`'s cluster creation.
- [x] 4.2 Run `openspec validate remove-elastic-and-keyvault --strict` and verify it passes. The change is then ready for `/opsx:archive`. Its sync REMOVES "Deployment configuration contract is preserved" and ADDS "Deployment configuration contract" (design D4), and it verifies that result itself.

  *Observed (2026-11-03):* `Change 'remove-elastic-and-keyvault' is valid` (exit 0), run on `f994a01` with the task notes above.
