## ADDED Requirements

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
