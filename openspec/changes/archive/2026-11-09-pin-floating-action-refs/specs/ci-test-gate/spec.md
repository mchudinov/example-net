## ADDED Requirements

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
