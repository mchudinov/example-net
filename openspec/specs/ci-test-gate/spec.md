# ci-test-gate Specification

## Purpose

Ensures that changes to the example-net application or its tests are built and tested automatically before and after they reach `main`, and that a release never publishes artifacts built from source whose tests fail.

Only a release publishes the Helm chart. Other runs check it, so a published chart version is never replaced.

## Requirements

### Requirement: Changes to application or test code are built and tested automatically
Every push to `main`, and every pull request that targets `main`, SHALL trigger an automated build-and-test run when it changes any of these:
- the application's source;
- the test project's source;
- the build configuration they share.

The run SHALL fail if the build fails, if any test fails, or if no tests are discovered. Each run that executes tests SHALL show a test summary on the run's page. It SHALL keep the test result files as a downloadable run artifact, including when tests fail.

#### Scenario: Passing change
- **WHEN** a pull request to `main` changes application source and all tests pass
- **THEN** the build-and-test check succeeds, and the run page shows a test summary with the total, passed and failed counts

#### Scenario: Failing test fails the check
- **WHEN** a push to `main` or a pull request to `main` contains a failing test
- **THEN** the build-and-test check fails, the run summary names the failing test, and the test result files are available as a run artifact

#### Scenario: No tests discovered fails the check
- **WHEN** a build-and-test run discovers zero tests
- **THEN** the check fails

#### Scenario: Unrelated change does not trigger a run
- **WHEN** a pull request to `main` changes only files outside the application, the test project and their shared build configuration, for example only files under `helm/`
- **THEN** no build-and-test run is triggered

### Requirement: Releases wait for passing tests
A release run SHALL build and test its source before it produces anything. A release run is a run of the release workflow started by a pushed `v*` tag.

If the build or any test fails, the release run SHALL produce nothing:
- no container image pushed;
- no image signed;
- no chart published or signed.

#### Scenario: Failing tests block the release
- **WHEN** a release run starts on source in which a test fails
- **THEN** no container image is pushed or signed, and no chart is published or signed

#### Scenario: Passing tests release as before
- **WHEN** a release run starts from a tag that passes the tag check, on source in which all tests pass and whose chart passes the chart checks
- **THEN** that run builds, pushes and signs the container image, then publishes and signs the chart at the tag version, and commits nothing to the repository

### Requirement: Only release runs publish a chart
The Helm chart SHALL be published and signed only by a release run, and only after that run has pushed and signed the container image. As defined under "Releases wait for passing tests", a release run is a run of the release workflow started by a pushed `v*` tag.

Every other run that processes the chart SHALL run the chart checks and SHALL fail if they fail. Such runs SHALL NOT publish or sign a chart. They include:
- a push to `main` that changes files under `helm/`;
- a pull request to `main` that changes files under `helm/`;
- a manual run of the chart workflow on its own.

As a result, a chart version that has been published is never replaced by a run that is not a release.

#### Scenario: Chart change pushed to main is checked, not published
- **WHEN** a push to `main` changes only files under `helm/`
- **THEN** the chart checks run against the pushed chart, no chart is published or signed, and the chart already published for the current chart version is unchanged

#### Scenario: Chart change in a pull request is checked, not published
- **WHEN** a pull request to `main` changes files under `helm/`
- **THEN** the chart checks run against the pull request's chart, and no chart is published or signed

#### Scenario: Manual chart run is checked, not published
- **WHEN** the chart workflow is started manually on its own, outside a release run
- **THEN** the chart checks run, and no chart is published or signed

#### Scenario: Release run still publishes the chart
- **WHEN** a release run from a `v*` tag succeeds
- **THEN** that same run publishes and signs the chart under the tag version, after it has pushed and signed the container image, as described in "Passing tests release as before"

### Requirement: Changes that affect the container image are checked by an image build
Every push to `main`, and every pull request that targets `main`, SHALL trigger an automated build of the container image when it changes any of these:
- the container build definition, or the list of files it excludes from the build;
- the application's source;
- the build configuration the image build uses.

The run SHALL build the image for every platform a release publishes. It SHALL fail if the image build fails for any of those platforms.

The run SHALL NOT publish anything:
- it SHALL NOT push or sign a container image;
- it SHALL NOT publish or sign a chart;
- it SHALL NOT commit to the repository;
- it SHALL NOT start a release run.

#### Scenario: Image builds for a change to its build definition
- **WHEN** a push to `main` changes the container build definition, and the image builds
- **THEN** the image build check succeeds, no container image is pushed or signed, and no chart is published or signed

#### Scenario: Application change is image-build checked
- **WHEN** a pull request to `main` changes application source
- **THEN** the image build check runs against the pull request's source, in addition to the build-and-test check

#### Scenario: Broken image build fails the check
- **WHEN** a push to `main` or a pull request to `main` contains a change with which the image no longer builds
- **THEN** the image build check fails

#### Scenario: Image build check covers every published platform
- **WHEN** the image build check runs
- **THEN** it builds the image for `linux/amd64` and `linux/arm64`

#### Scenario: A build that breaks on one platform fails the check
- **WHEN** a push to `main` or a pull request to `main` contains a change with which the image builds for one published platform but not the other
- **THEN** the image build check fails

#### Scenario: Change outside the image does not trigger an image build
- **WHEN** a push to `main` or a pull request to `main` changes only files that the image build does not use, for example only the test project's source or only files under `helm/`
- **THEN** no image build check is triggered

### Requirement: Chart checks cover a version with build metadata
Every run of the chart checks SHALL also check the chart at a version containing build metadata, in the form Flux produces. The run SHALL fail if either of these holds:
- a rendered label value isn't a valid Kubernetes label value, for value sets that together render every template that sets labels, including templates a default install doesn't create;
- the chart at that version can't be installed with its default values on the test cluster.

A label check that finds no labels to validate, or is given a chart whose version has no build metadata, SHALL fail rather than pass.

These checks SHALL NOT publish or sign anything. A run in which they fail SHALL NOT publish or sign a chart, and a release run in which they fail SHALL NOT push or sign a container image either.

A change to the chart workflow or to the label check SHALL itself run the chart checks.

#### Scenario: Valid chart passes
- **WHEN** the chart checks run on a chart whose rendered labels are all valid at a version containing build metadata
- **THEN** these checks succeed, and they publish nothing

#### Scenario: Raw version in a default template fails the check
- **WHEN** the chart checks run on a chart in which a template that a default install creates puts the unescaped chart version in a label
- **THEN** the check fails, and its output names the template and the label

#### Scenario: Raw version in a conditional template fails the check
- **WHEN** the chart checks run on a chart in which only a template that needs non-default values, such as the preview Service or the Rollout, puts the unescaped chart version in a label
- **THEN** the check fails, and its output names the template and the label

#### Scenario: Check cannot pass with nothing to check
- **WHEN** the label check is given a chart whose version has no build metadata, or a render that contains no labels
- **THEN** the check fails

#### Scenario: Failed check blocks publishing
- **WHEN** these checks fail during a release run
- **THEN** no container image is pushed or signed, and no chart is published or signed

#### Scenario: Change to the checks runs them
- **WHEN** a push to `main` changes only the chart workflow or the label check
- **THEN** the chart checks run, and no chart is published or signed

### Requirement: Third-party actions are never referenced by a branch
Every reference in a workflow to an action or reusable workflow from another repository SHALL name a release tag or a full commit SHA. It SHALL NOT name a branch.

The action that installs the signing tool SHALL be referenced by an exact release that can't be moved, or by a full commit SHA, so that a different version runs only after a commit in this repo changes the reference.

The chart checks SHALL audit the chart with the audit tool version that the chart workflow names, whichever release of the installing action the workflow references.

This requirement covers the references themselves. It doesn't cover files that an action or a step downloads while it runs.

#### Scenario: Workflows reference no branches
- **WHEN** the workflows in `.github/workflows/` are read
- **THEN** every reference to an action or reusable workflow in another repository names a release tag or a full commit SHA, and none names a branch

#### Scenario: Signing tool installer can't change without a commit
- **WHEN** a release run signs the container image or the chart
- **THEN** the action that installs the signing tool comes from an exact release that can't be moved, or from a full commit SHA

#### Scenario: Chart audit uses the version the workflow names
- **WHEN** the chart checks run
- **THEN** the audit runs with the audit tool version that the chart workflow names

### Requirement: Chart checks enforce the audit policy
Every run of the chart checks SHALL audit the chart with the repository's audit configuration, and not with the audit tool's default configuration.

The run SHALL fail if either of these holds:
- the audit reports a danger finding;
- the audit score is below the threshold that the chart workflow names.

A run in which the audit fails SHALL NOT publish or sign a chart, and a release run in which it fails SHALL NOT push or sign a container image either.

#### Scenario: Chart that meets the policy passes
- **WHEN** the chart checks run on a chart with no danger findings and a score at or above the threshold
- **THEN** the audit step succeeds

#### Scenario: Danger finding fails the check
- **WHEN** the chart checks run on a chart with a danger finding, for example a container that allows privilege escalation
- **THEN** the audit step fails, and its output names the finding

#### Scenario: Score below the threshold fails the check
- **WHEN** the chart checks run on a chart whose audit score is below the threshold
- **THEN** the audit step fails, and its output states the score and the threshold

#### Scenario: Repository configuration decides the checks
- **WHEN** the audit runs
- **THEN** the checks it evaluates, and their severities, are the ones in the repository's audit configuration

#### Scenario: Failed audit blocks publishing
- **WHEN** the audit fails during a release run
- **THEN** no container image is pushed or signed, and no chart is published or signed

### Requirement: Chart checks stop waiting for a test cluster that doesn't come up
Every run of the chart checks SHALL limit how long it waits for the test cluster to be ready. The chart workflow SHALL name the limit. The run SHALL fail if the test cluster isn't ready within the limit.

When creating the test cluster fails or reaches the limit, the run SHALL show in its log:
- each of the cluster's containers and its status;
- the logs of each of those containers.

A run in which the test cluster doesn't come up SHALL NOT publish or sign a chart, and a release run in which it doesn't come up SHALL NOT push or sign a container image either.

#### Scenario: Healthy cluster continues as before
- **WHEN** the chart checks run and the test cluster is ready within the limit
- **THEN** the checks continue, and the run's log shows no container logs from the cluster

#### Scenario: Stuck cluster fails at the limit
- **WHEN** a node of the test cluster doesn't become ready within the limit
- **THEN** the step that creates the cluster fails at the limit, and the run ends as failed instead of staying in progress

#### Scenario: Failed cluster leaves evidence
- **WHEN** creating the test cluster fails or reaches the limit
- **THEN** the run's log lists the cluster's containers with their status, and shows the logs of each one

#### Scenario: Other failures don't show cluster logs
- **WHEN** a chart check other than creating the test cluster fails
- **THEN** the run's log shows no container logs from the cluster

#### Scenario: Stuck cluster blocks publishing
- **WHEN** the test cluster doesn't come up during a release run
- **THEN** no container image is pushed or signed, and no chart is published or signed

### Requirement: Release tags name an exact version
Every release run SHALL check its tag before anything else in the run. The tag SHALL be `v` followed by three numbers separated by dots, for example `v1.3.2`. The numbers SHALL have no leading zeros, and the tag SHALL have no suffix.

A tag in any other form SHALL fail the check. This includes:
- a release candidate, such as `v1.3.2-rc.1`;
- a tag with build metadata, such as `v1.3.2+build.1`;
- a partial version, such as `v1.3`;
- any other tag that starts with `v`.

A tag that fails the check SHALL end the run as failed, and the run's log SHALL name the tag and the required form. That run SHALL NOT build, test or check anything, and SHALL produce nothing:
- no container image pushed;
- no image signed;
- no chart published or signed.

The release workflow SHALL start only from a pushed `v*` tag. It SHALL NOT be possible to start it manually.

#### Scenario: Exact version passes
- **WHEN** the tag `v1.3.2` is pushed
- **THEN** the tag check succeeds, and the release run continues with its build, tests and chart checks

#### Scenario: Release candidate is rejected
- **WHEN** the tag `v1.3.2-rc.1` is pushed
- **THEN** the tag check fails, and the run's log names the tag and the required form

#### Scenario: Other malformed tags are rejected
- **WHEN** a tag such as `v1.3`, `v01.3.2`, `v1.3.2+build.1` or `vnext` is pushed
- **THEN** the tag check fails

#### Scenario: Rejected tag publishes nothing
- **WHEN** a tag fails the tag check
- **THEN** no build, test or chart check runs, no container image is pushed or signed, and no chart is published or signed

#### Scenario: Release workflow can't be started manually
- **WHEN** the release workflow's triggers are read
- **THEN** a pushed `v*` tag is its only trigger

### Requirement: A release checks the chart before it pushes the image
Every release run SHALL run the chart checks on the chart from the tag's commit, and SHALL push the container image only after both the chart checks and the tests have passed. A release run SHALL run every chart check that a push or pull request runs on the chart.

If any chart check fails, the release run SHALL produce nothing:
- no container image pushed or signed;
- no chart published or signed.

#### Scenario: Failed chart check publishes nothing
- **WHEN** a release run starts from a tag that passes the tag check, on source in which all tests pass, and the chart fails a chart check
- **THEN** no container image is pushed or signed, and no chart is published or signed

#### Scenario: Release runs the same chart checks as a pull request
- **WHEN** the chart checks of a release run are compared with those of a pull request run on the same chart
- **THEN** the release run runs every chart check that the pull request run runs

### Requirement: The published chart is the package that was checked
A release run SHALL package the chart once, from the tag's commit. The chart checks SHALL lint that package and install it on the test cluster. The release run SHALL publish that same package file, and SHALL NOT package the chart again before publishing it.

#### Scenario: Published package is the installed package
- **WHEN** a release run publishes the chart
- **THEN** the published chart package is byte-for-byte the package that the run's chart checks linted and installed on the test cluster

#### Scenario: Later commits don't reach the chart
- **WHEN** a release tag is pushed on a commit that isn't `main`'s newest commit, or commits that change `helm/` reach `main` while the release run is in progress
- **THEN** the published chart is built from the chart in the tag's commit, and contains nothing from any other commit

### Requirement: The chart version comes from the release tag
A chart that a release publishes SHALL have the tag's version, without the leading `v`, as both its chart version and its app version, so that by default it installs the image published from the same tag. The release SHALL NOT take either version from the chart's source.

A release run SHALL NOT write anything to the repository: it SHALL NOT commit or push to any branch.

#### Scenario: Chart and image published from one tag
- **WHEN** the tag `v1.3.2` is pushed and the release run succeeds
- **THEN** the image and the chart are both published at version `1.3.2`, the chart's app version is `1.3.2`, and a default install of that chart uses the image `1.3.2`

#### Scenario: The chart's source declares a different version
- **WHEN** a tag that passes the tag check, such as `v1.3.2`, is pushed while the chart's source declares version `1.3.1`
- **THEN** the release does not fail on the difference, the chart is published at `1.3.2`, and the chart already published at `1.3.1` is not replaced

#### Scenario: A release writes nothing to the repository
- **WHEN** a release run succeeds
- **THEN** no branch, including `main`, has a new commit from that run
