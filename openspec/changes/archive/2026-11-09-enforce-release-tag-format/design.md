## Context

See `proposal.md` for the motivation and `specs/ci-test-gate/spec.md` for the required behavior.

Build-Docker starts on a pushed `v*` tag or a manual run. Its first job, `Test`, calls the reusable Build-Dotnet workflow, and every later job is chained behind `Test` through `needs`. Image tags come from `docker/metadata-action`:

- Valid semver, including a prerelease, generates image tags, and the release goes all the way through.
- Anything else generates no image tags. From reading the action's source, the push step should then fail after the tests have run. That path hasn't been run.

Build-Helm continues only after a successful Build-Docker run, so a failed run already stops the chart.

`actionlint` and `yq` aren't installed on this machine. A workflow's trigger and job chain can only be proven on GitHub.

## Goals / Non-Goals

**Goals:**

- Reject a malformed tag before any build, test or publishing step, and make the rejection visible as a failed run.
- Keep the check independent of the repository's contents and of any credentials.

**Non-Goals:**

- Removing a rejected tag from git. It stays until someone deletes it.
- Checking tags that don't start with `v`, such as `1.3.2`. They start no run, the same as today.
- Refusing a version that was already released. Pushing the same tag twice isn't possible without deleting it first.

## Decisions

### Keep the `v*` trigger and check inside a job

GitHub's tag filter could be narrowed to digits only, but then a bad tag would start no run and report nothing. A person who mistypes a tag would see no failure and could assume the release was on its way. A failed run says what went wrong.

### A separate first job that `Test` needs

`Test` calls a reusable workflow, so it can't hold a step of its own. The check can't go inside Build-Dotnet either, because that workflow also runs on pushes and pull requests, which have no tag.

### Inline script, no checkout, no permissions

The check needs only the ref that the runner already provides. A file under `.github/scripts/` would add a checkout for a few lines of shell. The job sets `permissions: {}`, because it needs no token.

### Match the full `GITHUB_REF` from the environment

The check matches `GITHUB_REF` rather than `GITHUB_REF_NAME`, so a branch named `v1.3.2` couldn't pass if a manual trigger were ever added back. It reads the variable from the environment instead of expanding `${{ github.ref }}` into the script, so a tag's text can't become shell code.

The pattern is:

```
^refs/tags/v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$
```

A simpler `[0-9]+` would accept `v01.3.2`, which isn't valid semver and would only fail later at the push step.

### Remove the manual trigger

The alternative was to keep the trigger and accept runs started on a tag, since GitHub lets you choose a tag for a manual run. That adds a second way to start a release with no need for one:

- **Re-run failed jobs** should cover a one-off failure.
- A failure that needs a commit takes the next patch tag.
- The registry has no `main` image, so no manual run from `main` has ever reached the push step.

Check-Tag still fails such a run if the trigger comes back, because of the full-ref match.

### Remove the branch and pull request tag rules

`type=ref,event=branch` and `type=ref,event=pr` can't produce anything once the workflow starts only from tags. The action's branch handler already returns nothing unless the ref is a branch, so removing them doesn't change a tag run's output.

Only the two `type=semver` lines stay. No `flavor` setting is added, so `latest` keeps being published.

## Risks / Trade-offs

- [The check wrongly rejects a valid tag] → It fails in seconds and publishes nothing, so the cost is a fix and a new tag. The script is run locally against valid and invalid tags before commit.
- [The path for a valid tag is first proven by a real release] → The local run covers the pattern, and the real test tag covers the trigger and job chain. A release that fails at this job has published nothing.
- [A real test tag could publish if the check were broken] → The test tag is `vtag-check-test`, which isn't semver. If the check failed to stop it, the action would generate no image tags, so the push should fail and nothing would be published.
- [A rejected tag stays in git] → Harmless, because nothing reads the list of tags. Delete it if it's in the way.

## Migration Plan

1. Commit the workflow and documentation changes to `main`. A push that changes only `build-docker.yaml` and documentation starts no workflow.
2. Push the test tag, confirm that the run fails at Check-Tag and that nothing is published, then delete the tag.
3. The next real release is the first full run through the new job.

To roll back, revert the commit. Runs already started keep the workflow file they started with.
