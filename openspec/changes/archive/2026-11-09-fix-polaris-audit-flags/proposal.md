## Why

The `Use Polaris` step in `Build-Helm` doesn't apply its score threshold or its config, and hasn't since at least 2024-03-05, when the step last changed. Its `run:` is a plain YAML scalar, so the line breaks fold into spaces and the backslashes stay in the command, and four flags reach Polaris as ordinary arguments. The audit reported 86 against a threshold of 90 and passed, while `CLAUDE.md` says it fails below 90.

## What Changes

- **`Use Polaris`:** `run:` becomes a block scalar (`run: |`), so every flag reaches Polaris. `--only-show-failed-tests` loses its stray `true`, because it takes no value.
- **What the audit then does:** it uses `.github/polaris-config.yaml` and enforces the threshold of 90. Today's chart scores 94 with no danger findings, so the step still passes, and neither the chart nor the threshold changes.
- **What it stops reporting:** four checks that the config doesn't list (`metadataAndInstanceMismatched`, `missingNetworkPolicy`, `missingPodDisruptionBudget`, `topologySpreadConstraint`).
- **What gets stricter:** `linuxHardening` becomes a danger check instead of a warning, and the chart passes it.

**Out of scope:**
- Adding those four checks to the config. Doing so would bring the score back down to 86.
- Fixing the five remaining warnings: `deploymentMissingReplicas` on the Deployment, and four on the `helm test` Pod.
- Upgrading the Polaris binary from 8.5.5, or checking the download against the published checksums.
- Editing `CLAUDE.md`. Line 142 becomes accurate once the step works.
- Cutting a release. No chart or image content changes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-test-gate`: adds a requirement that the chart checks enforce the audit policy, with the repository's configuration, a fail on any danger finding, and a fail below the score threshold.

## Impact

- **CI:** the `run:` of one step in `.github/workflows/build-helm.yaml`.
- **Unchanged:** the chart, `.github/polaris-config.yaml`, `POLARIS_EXIT_SCORE`, `POLARIS_VERSION`, `CLAUDE.md`, the application code and the tests. With no C# or project file changes, all the work stays in the main session.
- **From the push onwards:** a chart with a danger finding or a score below 90 fails `Build-Helm`, which blocks publishing. A chart change is checked on its own push to `main`, before any tag.
