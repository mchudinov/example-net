## ADDED Requirements

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
