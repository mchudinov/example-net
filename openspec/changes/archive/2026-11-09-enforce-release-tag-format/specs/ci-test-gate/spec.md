## ADDED Requirements

### Requirement: Release tags name an exact version
Every release run SHALL check its tag before anything else in the run. The tag SHALL be `v` followed by three numbers separated by dots, for example `v1.3.2`. The numbers SHALL have no leading zeros, and the tag SHALL have no suffix.

A tag in any other form SHALL fail the check. This includes:
- a release candidate, such as `v1.3.2-rc.1`;
- a tag with build metadata, such as `v1.3.2+build.1`;
- a partial version, such as `v1.3`;
- any other tag that starts with `v`.

A tag that fails the check SHALL end the run as failed, and the run's log SHALL name the tag and the required form. That run SHALL NOT build or test anything, and SHALL produce nothing:
- no container image pushed;
- no image signed;
- no chart version change committed;
- no chart published or signed as a follow-on of that release run.

The release workflow SHALL start only from a pushed `v*` tag. It SHALL NOT be possible to start it manually.

#### Scenario: Exact version passes
- **WHEN** the tag `v1.3.2` is pushed
- **THEN** the tag check succeeds, and the release run continues with its build and tests

#### Scenario: Release candidate is rejected
- **WHEN** the tag `v1.3.2-rc.1` is pushed
- **THEN** the tag check fails, and the run's log names the tag and the required form

#### Scenario: Other malformed tags are rejected
- **WHEN** a tag such as `v1.3`, `v01.3.2`, `v1.3.2+build.1` or `vnext` is pushed
- **THEN** the tag check fails

#### Scenario: Rejected tag publishes nothing
- **WHEN** a tag fails the tag check
- **THEN** no build or test runs, no container image is pushed or signed, no `Chart.yaml` version change is committed, and no chart is published or signed as a follow-on of that release run

#### Scenario: Release workflow can't be started manually
- **WHEN** the release workflow's triggers are read
- **THEN** a pushed `v*` tag is its only trigger

## MODIFIED Requirements

### Requirement: Releases wait for passing tests
A release run SHALL build and test its source before it produces anything. A release run is a run of the release workflow started by a pushed `v*` tag.

If the build or any test fails, the release run SHALL produce nothing:
- no container image pushed;
- no image signed;
- no chart version change committed;
- no chart published or signed as a follow-on of that release run.

#### Scenario: Failing tests block the release
- **WHEN** a release run starts on source in which a test fails
- **THEN** no container image is pushed or signed, no `Chart.yaml` version change is committed, and no chart is published or signed as a follow-on of that release run

#### Scenario: Passing tests release as before
- **WHEN** a release run starts from a tag that passes the tag check, on source in which all tests pass
- **THEN** the container image is built, pushed and signed, the chart version is updated to the tag version, and the chart is published and signed, as before this change

### Requirement: Only release runs publish a chart
The Helm chart SHALL be published and signed only as the follow-on of a successful release run. As defined under "Releases wait for passing tests", a release run is a run of the release workflow started by a pushed `v*` tag.

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
