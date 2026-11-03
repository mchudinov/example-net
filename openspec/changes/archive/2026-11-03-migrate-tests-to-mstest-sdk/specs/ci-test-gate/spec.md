## Purpose

Ensures that changes to the example-net application or its tests are built and tested automatically before and after they reach `main`, and that a release never publishes artifacts built from source whose tests fail.

## ADDED Requirements

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
