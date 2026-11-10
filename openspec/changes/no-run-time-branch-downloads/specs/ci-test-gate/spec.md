## MODIFIED Requirements

### Requirement: Third-party actions are never referenced by a branch
Every reference in a workflow to an action or reusable workflow from another repository SHALL name a release tag or a full commit SHA. It SHALL NOT name a branch.

No workflow step, and no action that a workflow uses, SHALL download a file from a branch of another repository while it runs. This applies to scripts, tools and configuration alike.

The action that installs the signing tool SHALL be referenced by an exact release that can't be moved, or by a full commit SHA, so that a different version runs only after a commit in this repo changes the reference.

The chart checks SHALL create the test cluster with the cluster tool version that the chart workflow names. They SHALL run that tool only if it matches the checksum that the chart workflow names. A different cluster tool then runs only after a commit in this repo changes the version and the checksum.

The chart checks SHALL audit the chart with the audit tool version that the chart workflow names, whichever release of the installing action the workflow references.

#### Scenario: Workflows reference no branches
- **WHEN** the workflows in `.github/workflows/` are read
- **THEN** every reference to an action or reusable workflow in another repository names a release tag or a full commit SHA, and none names a branch

#### Scenario: Nothing is downloaded from a branch
- **WHEN** the workflows in `.github/workflows/` are read, together with the scripts of each action they use at the referenced version
- **THEN** no step and no action script downloads a file from a branch of another repository

#### Scenario: Signing tool installer can't change without a commit
- **WHEN** a release run signs the container image or the chart
- **THEN** the action that installs the signing tool comes from an exact release that can't be moved, or from a full commit SHA

#### Scenario: Test cluster tool can't change without a commit
- **WHEN** the chart checks create the test cluster
- **THEN** the cluster tool that runs is the version the chart workflow names, and it matches the checksum the chart workflow names

#### Scenario: Cluster tool that doesn't match its checksum fails the check
- **WHEN** the downloaded cluster tool doesn't match the checksum that the chart workflow names
- **THEN** the chart checks fail before that tool runs

#### Scenario: Chart audit uses the version the workflow names
- **WHEN** the chart checks run
- **THEN** the audit runs with the audit tool version that the chart workflow names
