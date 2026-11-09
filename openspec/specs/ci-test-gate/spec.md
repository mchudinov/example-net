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
A release run SHALL build and test its source before it produces anything. A release run is either a pushed `v*` tag or a manual run of the release workflow.

If the build or any test fails, the release run SHALL produce nothing:
- no container image pushed;
- no image signed;
- no chart version change committed;
- no chart published or signed as a follow-on of that release run.

#### Scenario: Failing tests block the release
- **WHEN** a release run starts on source in which a test fails
- **THEN** no container image is pushed or signed, no `Chart.yaml` version change is committed, and no chart is published or signed as a follow-on of that release run

#### Scenario: Passing tests release as before
- **WHEN** a release run starts from a `v*` tag on source in which all tests pass
- **THEN** the container image is built, pushed and signed, the chart version is updated to the tag version, and the chart is published and signed, as before this change

### Requirement: Only release runs publish a chart
The Helm chart SHALL be published and signed only as the follow-on of a successful release run. As defined under "Releases wait for passing tests", a release run is either a pushed `v*` tag or a manual run of the release workflow.

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
- **WHEN** the chart workflow is started manually on its own, not as the follow-on of a release run
- **THEN** the chart checks run, and no chart is published or signed

#### Scenario: Release run still publishes the chart
- **WHEN** a release run from a `v*` tag succeeds
- **THEN** the chart is published and signed under the tag version, as described in "Passing tests release as before"

### Requirement: Changes that affect the container image are checked by an image build
Every push to `main`, and every pull request that targets `main`, SHALL trigger an automated build of the container image when it changes any of these:
- the container build definition, or the list of files it excludes from the build;
- the application's source;
- the build configuration the image build uses.

The run SHALL build the image for every platform a release publishes. It SHALL fail if the image build fails for any of those platforms.

The run SHALL NOT publish anything:
- it SHALL NOT push or sign a container image;
- it SHALL NOT commit a `Chart.yaml` version change;
- it SHALL NOT start a release run or the publishing of a chart.

#### Scenario: Image builds for a change to its build definition
- **WHEN** a push to `main` changes the container build definition, and the image builds
- **THEN** the image build check succeeds, no container image is pushed or signed, and no chart is published or signed as a follow-on of that run

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

These checks SHALL NOT publish or sign anything. A run in which they fail SHALL NOT publish or sign a chart.

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
- **WHEN** these checks fail during the chart run that follows a release
- **THEN** no chart is published or signed

#### Scenario: Change to the checks runs them
- **WHEN** a push to `main` changes only the chart workflow or the label check
- **THEN** the chart checks run, and no chart is published or signed
