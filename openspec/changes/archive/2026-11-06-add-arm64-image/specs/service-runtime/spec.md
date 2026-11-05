## ADDED Requirements

### Requirement: Container image is published for amd64 and arm64
Each release SHALL publish the container image for `linux/amd64` and `linux/arm64`. Its version tags SHALL point at one multi-platform image, so a client that pulls a version tag gets the image for its own architecture. That multi-platform image's digest SHALL be signed.

Neither platform's image SHALL contain a binary built specifically for a different architecture. Both SHALL contain the same set of files in the application directory.

#### Scenario: Version tag lists both platforms
- **WHEN** the manifest of a released version tag is inspected
- **THEN** it lists an image for `linux/amd64` and an image for `linux/arm64`

#### Scenario: Signature verifies for the released image
- **WHEN** the release's signature is verified against the digest that a released version tag resolves to
- **THEN** verification succeeds

#### Scenario: Each image contains no binary built for another architecture
- **WHEN** the application directory (`/App`) of each platform's image is inspected without starting the container
- **THEN** it contains no executable or library built specifically for a different architecture

#### Scenario: Both platforms ship the same files
- **WHEN** the file listings of the application directory (`/App`) in the two platforms' images are compared
- **THEN** the listings are identical
