## ADDED Requirements

### Requirement: Changes that affect the container image are checked by an image build
Every push to `main`, and every pull request that targets `main`, SHALL trigger an automated build of the container image when it changes any of these:
- the container build definition, or the list of files it excludes from the build;
- the application's source;
- the build configuration the image build uses.

The run SHALL fail if the image build fails.

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

#### Scenario: Change outside the image does not trigger an image build
- **WHEN** a push to `main` or a pull request to `main` changes only files that the image build does not use, for example only the test project's source or only files under `helm/`
- **THEN** no image build check is triggered
