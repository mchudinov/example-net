## ADDED Requirements

### Requirement: Chart checks stop waiting for a test cluster that doesn't come up
Every run of the chart checks SHALL limit how long it waits for the test cluster to be ready. The chart workflow SHALL name the limit. The run SHALL fail if the test cluster isn't ready within the limit.

When creating the test cluster fails or reaches the limit, the run SHALL show in its log:
- each of the cluster's containers and its status;
- the logs of each of those containers.

A run in which the test cluster doesn't come up SHALL NOT publish or sign a chart.

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
- **WHEN** the test cluster doesn't come up during the chart run that follows a release
- **THEN** no chart is published or signed
