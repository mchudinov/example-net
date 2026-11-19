## ADDED Requirements

### Requirement: Text files reach verification with LF line endings
The repository SHALL store every text file with LF line endings, whichever platform it was edited on and whatever the local Git configuration says. It SHALL check text files out with LF on every platform. The only exception is a file whose repository attributes declare CRLF for the working tree; it is still stored with LF. No file declares CRLF today.

A file is then the same, byte for byte, in a local checkout and in verification. A tool that reads it line by line gives the same answer in both places, whether it matches a pattern anchored at the end of a line or reads a version out of the file.

Every push to `main`, and every pull request that targets `main`, SHALL run a line-ending check, whichever files it changes. The check SHALL fail when a committed text file is stored with CRLF line endings in any of its lines, and SHALL name every such file. In normal work the rule above keeps such a file out of a commit. The check catches the rule being bypassed or removed. Files the repository treats as binary, whether by their content or by their attributes, are stored as they are, and the check SHALL NOT report them.

Every release run SHALL run the same check on the tag's commit, and SHALL push the container image only after the check has passed. If the check fails, the release run SHALL produce nothing:
- no container image pushed or signed;
- no chart published or signed.

The check SHALL NOT publish or sign anything.

#### Scenario: A file saved with CRLF is committed
- **WHEN** a text file saved with CRLF line endings, on any platform, is committed
- **THEN** it is stored with LF line endings, and a checkout of it on any platform has LF line endings

#### Scenario: A committed text file has CRLF line endings
- **WHEN** a push to `main` or a pull request to `main` contains a text file stored with CRLF line endings in some or all of its lines
- **THEN** the line-ending check fails, and its output names the file

#### Scenario: A change that no other check covers is checked
- **WHEN** a push to `main` changes only files that no other check covers, for example only documentation
- **THEN** the line-ending check runs

#### Scenario: A file declared as CRLF in the working tree
- **WHEN** a text file whose repository attributes declare CRLF for the working tree is committed
- **THEN** it is stored with LF line endings, a checkout of it has CRLF line endings, and the line-ending check does not report it

#### Scenario: A binary file is committed
- **WHEN** a file that the repository treats as binary, by its content or by its attributes, is committed
- **THEN** its bytes are stored unchanged, and the line-ending check does not report it, even if it contains CRLF byte pairs

#### Scenario: A CRLF file blocks the release
- **WHEN** a release run starts from a tag that passes the tag check, on a commit that contains a text file stored with CRLF line endings
- **THEN** no container image is pushed or signed, and no chart is published or signed

## MODIFIED Requirements

### Requirement: Releases wait for passing tests
A release run SHALL build and test its source before it produces anything. A release run is a run of the release workflow started by a pushed `v*` tag.

If the build or any test fails, the release run SHALL produce nothing:
- no container image pushed;
- no image signed;
- no chart published or signed.

#### Scenario: Failing tests block the release
- **WHEN** a release run starts on source in which a test fails
- **THEN** no container image is pushed or signed, and no chart is published or signed

#### Scenario: Passing tests release as before
- **WHEN** a release run starts from a tag that passes the tag check, on source in which all tests pass, whose chart passes the chart checks, and whose text files pass the line-ending check
- **THEN** that run builds, pushes and signs the container image, then publishes and signs the chart at the tag version, and commits nothing to the repository
