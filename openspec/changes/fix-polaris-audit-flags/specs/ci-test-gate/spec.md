## ADDED Requirements

### Requirement: Chart checks enforce the audit policy
Every run of the chart checks SHALL audit the chart with the repository's audit configuration, and not with the audit tool's default configuration.

The run SHALL fail if either of these holds:
- the audit reports a danger finding;
- the audit score is below the threshold that the chart workflow names.

A run in which the audit fails SHALL NOT publish or sign a chart.

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
- **WHEN** the audit fails during the chart run that follows a release
- **THEN** no chart is published or signed
