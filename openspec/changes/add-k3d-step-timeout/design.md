## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-09:

- **The step.** `.github/workflows/build-helm.yaml:68` uses `AbsaOSS/k3d-action@v2`, a composite action with a single `run.sh deploy` step and no post step. `run.sh` runs `k3d cluster create` with `--wait`. Nothing under `.github/` sets `timeout-minutes` or `--timeout`.
- **How long it takes.** The last 14 successful runs took 26 to 47 s on this step, and the re-run after the hang took 35 s.
- **The hang.** In run 42656239890, attempt 1, the last k3d line is `Starting node 'k3d-test-cluster-agent-0'` at 16:35:25, and nothing follows until the cancel at 19:18:50. In a passing run the agent takes about 13 s and the load balancer starts after it, so the load balancer never started in the hung run. The cause is unknown.
- **What the log rules out.** k3d's wait loop prints "is restarting for more than" while a node restarts, and "encountered fatal log" when a node's log stream ends on a fatal line. Neither appears in 2 h 43 min, so this doesn't look like a crash loop. Either the agent kept running without printing its ready line, or k3d's log read stalled. The container logs went with the runner.
- **What k3d offers (v5.9.0).**
  - `--timeout` defaults to 0, and `--wait` defaults to true.
  - On failure, k3d deletes the cluster unless `--no-rollback` is set (`cmd/cluster/clusterCreate.go:157-166`).
  - Its wait loop checks the deadline when a log line arrives (`pkg/client/node.go:849`). A silent node is interrupted only if the Docker client aborts the blocked read, which hasn't been checked.
  - Every container it creates has the label `app=k3d` (`pkg/types/defaults.go:44`).
- **What the runner does.** `timeout-minutes` applies to any step. On expiry the step's result is `Failed`, with the message `The action '<name>' has timed out after <n> minutes.` (`actions/runner`, `src/Runner.Worker/StepsRunner.cs:296-329`). The composite handler takes the same branch (`Handlers/CompositeActionHandler.cs:495-502`). A failed step lets `if: failure()` steps run.
- **What a re-run does.** GitHub's docs say a re-run keeps the original `GITHUB_SHA` and `GITHUB_REF`, that re-running failed jobs also re-runs the jobs that depend on them, and that a run can be re-run for up to 30 days.
  - The last release-triggered run, 42621519820, ran on `376a2cb`, the bot's `Chart.yaml` commit, so a re-run would package the right chart version.
  - Runs 42656239890 and 41692729560 kept their `push` event on attempt 2.
  - None of the 7 release-triggered runs has ever been re-run.
- **What exercises the step.** `Build-Helm` triggers on its own workflow file, so a push runs the step and publishes nothing.

## Goals / Non-Goals

**Goals:**
- A stuck test cluster ends in a failed run within minutes.
- A stuck test cluster leaves what's needed to find out why.

**Non-Goals:**
- **Preventing a hang.** The cause is unknown, and this change doesn't address it.
- **Keeping a release whole.** A hang during a release still leaves the image out without a chart until someone acts. This change makes that visible sooner and shortens the recovery.

## Decisions

Each decision is marked *agreed* (decided with the user) or *planning default* (proposed, open to change at review).

### D1. The limit is `timeout-minutes` on the step (agreed)

The runner enforces it, whatever k3d is doing, and it covers k3d's download too.

**Alternatives considered:**
- **k3d's `--timeout` in `args`.** It gives k3d's own error message, but its rollback deletes the containers whose logs are wanted, and it isn't known to interrupt a silent node.
- **`timeout-minutes` on the job.** It would have to be far looser, because one older run took about 13 minutes outside the k3d step, and it wouldn't say which step stuck.

### D2. The limit is 5 minutes (agreed)

That's about six times the slowest of the last 14 runs (47 s). It's written in the step, because nothing else uses it.

### D3. A separate step shows the evidence, only when the k3d step failed (agreed)

The condition is `if: failure() && steps.k3d.outcome == 'failure'`. `failure()` is needed because a condition with no status function gets an implicit `success()`, and the outcome check keeps every other failure out of it. A manual cancel doesn't run the step, which is acceptable because the limit arrives first.

### D4. The step shows every k3d container and its whole log (planning default)

It finds the containers by the `app=k3d` label. Each `docker logs` is followed by `|| true`, so one container with no logs doesn't end the loop.

**Alternatives considered:**
- **Hard-coded container names, as first sketched.** They would print nothing after a rename, and they'd skip the load balancer.
- **`--tail 200`, as first sketched.** A stuck node can repeat one line until the start of its log falls out of the tail, and the cause may be there. The 5-minute limit already bounds the log's size.

### D5. `CLAUDE.md` names both ways to recover (agreed)

The sentence at `CLAUDE.md:148` becomes:

> **If a release's chart publishing fails,** use **Re-run failed jobs** on that Build-Helm run when the cause was a one-off, such as a stuck k3d cluster. It keeps the release's commit and should publish, but that path hasn't been used on a release yet. If the cause needs a commit, fix it and push the next patch tag. A manual run can't publish.

It says "failed jobs" on purpose, because re-running every job of a successful release run would push the same chart version again.

### D6. A temporary commit proves the failure path on a real runner (planning default)

The commit adds `--k3s-arg "--server=https://192.0.2.1:6443@agent:0"` to `args`, which points the agent at an address that can't answer, so it should never register. It's tried locally first, since a trigger that doesn't hang shouldn't cost any commits.

**Alternatives considered:**
- **No forced hang.** The runner's code shows the step will fail, but only a real runner shows whether the containers survive the kill and whether their logs appear.
- **A throwaway branch.** It would keep `main` clean, but this repo works directly on `main`.
- **A shorter limit for the test.** It wouldn't exercise the real value.

### D7. This change doesn't cut a release (planning default)

No chart or image content changes, and a push exercises the step.

### D8. No branch or pull request (agreed)

The work goes directly on `main`, as this repo does. The user is asked before each push.

## Risks / Trade-offs

- **[The limit fails a healthy run]** A slow runner could pass 5 minutes. → It's about six times the slowest recent run, and a false failure costs one re-run.
- **[The kill removes the containers]** k3d's entry points show no signal handler, and today's cancel printed no rollback, but neither proves the containers survive. → Task 4.2 checks it. If they're gone, the limit still works and D4 is revisited.
- **[Re-running is unproven on a release]** It can't be tested without publishing over a released chart. → `CLAUDE.md` says so, and the next patch tag still works if a re-run doesn't publish.
- **[Node logs are public]** The repository is public. → The logs come from a cluster that's thrown away with the runner, and no repository secret goes into it.
- **[A tag during the forced hang]** A release between the temporary commit and its revert would stop at the chart checks. → Task 4.1 forbids a tag until the revert is pushed, a window of about ten minutes.
- **[The trigger doesn't hang on the runner]** The step would pass or fail fast and prove nothing. → Task 4.2 stops there, the commit is reverted, and the user decides what's next.
- **[Temporary commits in the history]** `main` gets one temporary commit and its revert. → Accepted (D6).

## Migration Plan

1. The change lands on `main`. `Build-Helm` runs the k3d step with the limit and publishes nothing.
2. A temporary commit forces a hang, the log shows the failure path, and a revert restores the step.

**Rollback:** revert the commit, which restores the step without a limit. Nothing published depends on this change.

## Open Questions

- **Who does GitHub notify when a release-triggered run fails?** Until that's known, someone may still have to spot the red run. The answer doesn't change this change.
