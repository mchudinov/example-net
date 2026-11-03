## ADDED Requirements

### Requirement: Container image contains only the application
The container image's application directory SHALL contain only the application's own publish output. It SHALL NOT contain test assemblies, test frameworks, test runners or code-coverage tooling.

#### Scenario: Application directory has no test payload
- **WHEN** the files under the image's application directory (`/App`) are listed
- **THEN** none of them belongs to the test project, a test framework, a test runner or code-coverage tooling. For example, there are no `ExampleNetTests.*`, `MSTest*`, `testhost.*`, `Microsoft.TestPlatform.*`, `Microsoft.Testing.*`, `coverlet.*` or `Microsoft.CodeCoverage` entries
