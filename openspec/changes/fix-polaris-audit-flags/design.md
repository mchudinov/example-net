## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-09:

- **The step.** `.github/workflows/build-helm.yaml:126` is the only `run:` in the workflows that is a plain scalar ending in a backslash. Every other multi-line `run:` in that file uses `|`.
- **What reaches Polaris.** CI logs the command with the backslashes still in it. Bash reads each `\ ` as an escaped space, so the next word gets a leading space and isn't parsed as a flag.

  ```
  flag                            reaches Polaris as a flag?
  --helm-chart                    yes
  --helm-values                   no   (harmless: it names the chart's own values.yaml)
  --only-show-failed-tests        no   (output only)
  --format=pretty                 yes
  --set-exit-code-on-danger       yes
  --set-exit-code-below-score     no   (no score gate)
  --config                        no   (Polaris's defaults are used)
  ```

- **Local audit results.** Polaris 8.5.5 (checksum verified against the release's `checksums.txt`) with Helm 4.1.4, on today's chart:

  ```
  command                              config     score  danger  exit
  as CI runs it today                  defaults   86     0       0
  corrected                            this repo  94     0       0
  corrected, without --config          defaults   86     0       4
  corrected, threshold raised to 95    this repo  94     0       4
  corrected, privilege escalation on   this repo  91     1       3
  ```

  The first row reproduces run 42646174236: score 86, exit 0, and passing checks printed despite `--only-show-failed-tests`.
- **Which checks run.** Across the Deployment and the `helm test` Pod, this repo's config evaluates 45 checks and the defaults evaluate 52. The difference is the four check IDs in proposal.md, which account for 7 warnings and the move from 86 to 94. `missingNetworkPolicy` is commented out in the config, and the other three aren't listed. `linuxHardening` is danger under this repo's config and warning under the defaults.
- **Remaining warnings.** There are five: `deploymentMissingReplicas` on the Deployment, and `automountServiceAccountToken`, `priorityClassNotSet`, `readinessProbeMissing` and `livenessProbeMissing` on the test Pod. The `pretty` output with `--only-show-failed-tests` lists only the Pod's four, and the score and the JSON output count all five.
- **Failure path.** The step runs under `bash -e`, so exit 3 (danger) or 4 (score) fails it. `Build-and-Push-Helm` needs `Check-Helm`, and `Sign-Helm-Chart` needs both.
- **What exercises the step.** `Build-Helm` triggers on its own workflow file, so a push runs the corrected step and publishes nothing.
- **Limit of this evidence.** The local runs used the Windows binary, and CI uses the Linux one with the Helm that `azure/setup-helm` installs. The control row matches CI. The corrected command hasn't run on CI yet.

## Goals / Non-Goals

**Goals:**
- Every flag written in the step reaches Polaris.
- The audit can fail a chart, for the reasons `CLAUDE.md` already gives.

**Non-Goals:**
- **Raising the chart's score.** It already clears the threshold.
- **Changing what the audit policy is.** The config and the threshold were chosen before this change, and this change only makes them apply.

## Decisions

Each decision is marked *agreed* (decided with the user) or *planning default* (proposed, open to change at review).

### D1. The step uses a block scalar (planning default)

`run: |` keeps the line breaks, so each backslash continues the line as written. It matches every other multi-line `run:` in the file.

**Alternatives considered:**
- **A folded scalar without backslashes (`>-`).** It works, but it's the only one of its kind in the file, and a later edit that adds a backslash would bring the bug back.
- **One long line.** It's about 200 characters and hard to review.

### D2. Neither the chart nor the threshold changes (planning default)

The brief left this open, assuming the corrected audit would stay at 86. It scores 94 with no danger findings, because applying the config also removes the 7 warnings that kept it at 86. There's nothing to fix and no reason to move the threshold.

### D3. `.github/polaris-config.yaml` stays as it is (planning default)

Applying it drops four checks from the audit. That's the file's current scope, and one of the four is deliberately commented out. Adding them is a policy decision, and would need a lower threshold or chart work, since the score would return to 86.

### D4. `--only-show-failed-tests` loses its `true` (planning default)

It's a boolean flag, so `true` would be a stray positional argument. `--helm-values` stays even though it's redundant, because the step already names it and it's harmless.

### D5. `CLAUDE.md` isn't edited (planning default)

The brief asked to correct line 142. It says the audit uses the config and fails on danger or below 90, and that becomes true with this change.

### D6. This change doesn't cut a release (planning default)

No chart or image content changes, and a push proves the step works.

### D7. No branch or pull request (agreed)

The work goes directly on `main`, as this repo does. The user is asked before the push.

## Risks / Trade-offs

- **[The corrected command scores differently on the runner]** The local runs used a different platform. → The push publishes nothing, the task checks for 94 in the CI log, and a failure is fixed or reverted before any tag.
- **[The margin is four points]** A later chart change could fail `Build-Helm`. → That's the gate working, and the change is checked on its own push before any tag.
- **[An audit failure after a tag]** The image would already be out, with no chart, and the fix would be the next patch tag. → A chart can only reach a tag after its push ran the same audit.
- **[Four checks stop being reported]** Their findings disappear from the log. → Accepted (D3).
- **[The failing cases can't be shown on live CI]** That would take a broken commit on `main`. → Tasks prove them locally with the step's own text, and record that they weren't run live.
- **[The log under-reports one warning]** Readers see four warnings and a score that counts five. → Accepted. It's how the tool's `pretty` format behaves.

## Migration Plan

1. The change lands on `main`, before the next tag. `Build-Helm` runs the corrected audit and publishes nothing.
2. The CI log shows the score of 94, confirming that the local result holds on the runner.

**Rollback:** revert the commit, which restores the step that doesn't enforce anything. Nothing published depends on this step's flags.
