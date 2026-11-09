All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main` (design D8). Ask the user before each push.

Run commands from the repo root in Git Bash unless a task says otherwise. In these tasks:
- `<scratchpad>` is the session's scratchpad directory.
- `<k3d>` is the k3d v5.9.0 binary from task 1.1.
- `<trigger>` is `--k3s-arg "--server=https://192.0.2.1:6443@agent:0"` (design D6).

## 1. Prove the hang trigger locally

- [x] 1.1 Into a new directory under `<scratchpad>`, run `gh release download v5.9.0 --repo k3d-io/k3d --pattern k3d-windows-amd64.exe --pattern checksums.txt`. Verify that the binary's SHA-256 matches `checksums.txt`, and that `<k3d> version` reports `v5.9.0`.

  *Result (2026-11-09):* SHA-256 `49d0b9c7…80a034` matches `checksums.txt`, and the version is `v5.9.0` with k3s `v1.35.5-k3s1`, the k3s version CI uses.
- [x] 1.2 With Docker running, run `<k3d> cluster create hang-test --agents 1 --wait --timeout 180s <trigger>`. Verify that the output reaches `Starting node 'k3d-hang-test-agent-0'`, never reaches `Starting helpers`, and that the command exits non-zero at about 180 s. Then verify that `docker ps -a --filter label=app=k3d` lists nothing, and run `<k3d> cluster delete hang-test` if it does. If k3d can't run on this machine, record why and rely on task 4.2. If the cluster comes up or the command fails fast, stop and tell the user, because the trigger doesn't hang.

  *Result (2026-11-09, fallback taken, trigger not proven locally):* k3d can't run on this machine. It stopped at `Prep: Network` and failed at 180 s with `context deadline exceeded` on `POST /networks/create`, before any node existed. A plain `docker network create` also timed out, at 30 s, while the rest of the Docker Desktop API responded, so the problem is local Docker and not k3d or the trigger. Docker Desktop was left alone. No k3d containers, volumes or networks remain. Task 4.2 is the first real test of the trigger.

## 2. Make the change

- [x] 2.1 In `.github/workflows/build-helm.yaml`, add the comment, the `id` and the `timeout-minutes` to the k3d step, leaving its `with:` block as it is:

  ```yaml
      # k3d waits for its nodes with no limit of its own. The timeout kills only the k3d client, so the containers stay for the next step.
      - name: Create a test k3d cluster for dry-run install
        id: k3d
        uses: AbsaOSS/k3d-action@v2
        timeout-minutes: 5
  ```

  Verify by reading the diff that `cluster-name`, `k3d-version` and `args` are unchanged.

  *Result (2026-11-09):* the diff adds the comment, `id: k3d` and `timeout-minutes: 5`, and doesn't touch the `with:` block.
- [x] 2.2 Directly after that step, before `Dry-run install`, add:

  ```yaml
      - name: Show the k3d containers and logs after a failed cluster start
        if: failure() && steps.k3d.outcome == 'failure'
        run: |
          docker ps -a --filter label=app=k3d
          for container in $(docker ps -aq --filter label=app=k3d); do
            echo "::group::$(docker inspect --format '{{.Name}}' "$container")"
            docker logs "$container" || true
            echo "::endgroup::"
          done
  ```

  Verify by reading the file that no other step moved, and that the `if:` matches design D3.

  *Result (2026-11-09):* the step sits directly before `Dry-run install`, the diff only adds lines around it, and the `if:` matches D3.
- [x] 2.3 In `CLAUDE.md`, replace the sentence at line 148 with the one quoted in design D5. Verify that `git diff --stat -- .github CLAUDE.md` lists only `build-helm.yaml` and `CLAUDE.md`.

  *Result (2026-11-09):* the sentence is replaced, and the stat lists those two files, at 14 insertions and 1 deletion.
- [x] 2.4 Verify that `openspec validate add-k3d-step-timeout --strict` passes.

  *Result (2026-11-09):* `Change 'add-k3d-step-timeout' is valid`.

## 3. Check the healthy path on CI

- [x] 3.1 Commit on `main`, staging only `.github/workflows/build-helm.yaml`, `CLAUDE.md` and this change's folder, because the two archived changes may still be uncommitted. Ask the user, and push. Verify with `gh run list --commit <sha>` and the run's jobs that `Build-Helm` succeeded, that the k3d step finished within the limit, that the new step was skipped, that `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped, and that `Build-Docker` didn't run (spec: "Healthy cluster continues as before"). If the run fails, stop and tell the user, and don't start section 4.

  *Result (2026-11-09):* commit `5d2e521`, run 42690865967, the only run for that commit. `Build-Helm` succeeded, and the k3d step took 37 s. `Show the k3d containers and logs after a failed cluster start` was skipped, `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped, and `Build-Docker` didn't run.

## 4. Force a hang on CI

- [x] 4.1 In `.github/workflows/build-helm.yaml`, change `args` to `args: --agents 1 <trigger>`. Commit that change alone, with a subject that says it's temporary. Ask the user, and push. Don't push a tag until task 4.3 is pushed (design Risks).

  *Result (2026-11-09):* commit `5a9da82`, which changes only the `args` line. It started run 42691234333.
- [x] 4.2 In that run, verify all of these:
  - The k3d step failed at about 5 minutes with `has timed out after 5 minutes`, and the run's conclusion is `failure` (spec: "Stuck cluster fails at the limit").
  - The new step ran, lists `k3d-test-cluster-agent-0` and `k3d-test-cluster-server-0` with their status, and shows their logs (spec: "Failed cluster leaves evidence").
  - The steps after it in `Check-Helm` didn't run.

  If the step passed or failed fast, the trigger didn't hang on the runner, so go to task 4.3, then stop and tell the user. If the containers are missing, record that and tell the user after task 4.3 (design Risks).

  *Result (2026-11-09):* run 42691234333, conclusion `failure`. The k3d step failed after 5 min 12 s. Its message is `The action has timed out.`, not the `has timed out after 5 minutes` this task expected, because the runner's composite handler words it differently (design Context is corrected). The new step ran and lists `k3d-test-cluster-agent-0` (`Up 4 minutes`), `k3d-test-cluster-server-0` (`Up 5 minutes`) and `k3d-test-cluster-serverlb` (`Created`). It shows 3 log lines for the agent, 640 for the server and none for the load balancer, which never started. The containers survived the kill, and k3d printed no rollback. `Dry-run install` and every later step in `Check-Helm` were skipped. The agent printed its 3 lines in its first second and nothing after that, so this also covered a silent node. The printed logs contain no token values, and `GHCR_PASSWORD` is masked.
- [x] 4.3 Revert the temporary commit with `git revert`. Ask the user, and push. Verify that `git diff <task 3.1 sha> -- .github/workflows/build-helm.yaml` is empty, and that `Build-Helm` succeeds with the new step skipped.

  *Result (2026-11-09):* commit `7ffc48c`, run 42703193283. The diff against `5d2e521` is empty. `Build-Helm` succeeded, the k3d step took 34 s, the new step was skipped, and `Build-and-Push-Helm` and `Sign-Helm-Chart` were skipped. The temporary commit was on `main` for 75 minutes (20:15:20 to 21:30:33 UTC), not the ten planned, while the revert waited for the push to be confirmed. No tag was pushed in that window, the newest tag is still `v1.3.1`, and the forced-hang run was the only run that started on it.

## 5. Record what wasn't run live

- [x] 5.1 Verify by reading `.github/workflows/build-helm.yaml` that `Build-and-Push-Helm` needs `Check-Helm` and `Sign-Helm-Chart` needs both (spec: "Stuck cluster blocks publishing"), because on a push those jobs skip whatever `Check-Helm` does.

  *Result (2026-11-09, from reading the workflow):* line 147 reads `needs: Check-Helm`, and line 176 reads `needs: [Check-Helm, Build-and-Push-Helm]`.
- [x] 5.2 Verify by reading the `if:` that an earlier failure leaves the k3d step `skipped` and a later failure leaves it `success`, so neither runs the new step (spec: "Other failures don't show cluster logs"). Record that 5.1 and 5.2 are evidence from reading the workflow, and that re-running a failed release run hasn't been exercised (design Risks).

  *Result (2026-11-09, from reading the workflow):* line 81 reads `if: failure() && steps.k3d.outcome == 'failure'`. The k3d step has no `if:`, so an earlier failure skips it and its outcome is `skipped`. The new step comes directly after it, so a later failure happens after the new step has already been skipped. 5.1 and 5.2 weren't run live as failures, and re-running a failed release run hasn't been exercised.
