# Code signing

*The last item on the roadmap. Opened on 2026-08-29.*

## The problem, in one sentence

The Optimus installer is not signed. SmartScreen therefore warns everyone who downloads it —
“Windows protected your PC”, with a *Don't run* button up front and *More info* hidden — and
Smart App Control, on by default on some Windows 11 installations, refuses it outright. **Until
that signature exists, public distribution stays blocked** (risk R16).

The installer did not solve this problem, it concentrated it: there is now **one** file to sign
instead of twenty-four.

## Why SignPath, and not a purchased certificate

Since June 2023 the CA/Browser Forum requires the private key of a code signing certificate to
live in certified hardware — a USB token or a cloud HSM. Prices followed: reckon on **€200 to
€600 a year**, which the maintainer explicitly ruled out.

A self-signed certificate is useless: SmartScreen does not judge the presence of a signature but
the **reputation** of the authority that issued it, and an unknown authority has none.

That leaves the **SignPath Foundation**, which signs free of charge for open source projects. It
does not issue a certificate: it signs itself, from its own infrastructure, artefacts submitted
to it by a public build pipeline. Nobody — the maintainer included — ever holds the key. That is
what makes the service sustainable for them, and it is also what dictates everything else in this
document.

## What the foundation requires

Taken from [signpath.org/terms.html](https://signpath.org/terms.html) on 2026-08-29.

| Condition | Where Optimus stands |
|---|---|
| No malware | ✅ |
| **OSI-approved open source licence**, without commercial dual licensing | ✅ GPL-3.0, chosen on 2026-08-29 |
| No proprietary component | ✅ Piper and whisper.cpp are MIT, downloaded rather than embedded |
| Actively maintained project | ✅ |
| **Publicly available codebase** | ✅ public since 2026-08-29 |
| **Already released in the form to be signed** | ❌ no public release yet |
| Functionality described on the download page | ✅ README rewritten for it on 2026-08-29 |
| The signing team is the development team, and owns the repository | ✅ |
| Only sign what you built yourself | ✅ |
| **Multi-factor authentication** for every contributor | ⚠️ to be confirmed on the GitHub account |
| Defined roles: authors, reviewers, approvers | ✅ see below |
| **Signing policy published** on the project's home page | ✅ see below |
| Automated, verifiable builds | ✅ `.github/workflows/release.yml` |

**Maintainer's decision, 2026-08-29: public repository under GPL-3.0.** Copyleft was preferred
over permissive licences for one precise reason — it prevents anyone taking Optimus and turning
it into a closed, paid product, while constraining nobody who simply uses it. Worth noting: since
the foundation forbids commercial dual licensing, that road is closed for good, and that is
accepted.

## The build pipeline

`.github/workflows/release.yml` triggers on a `vX.Y.Z` tag and runs, in order:

1. the tests — an installer does not come out of code that fails;
2. publishing the application;
3. **signing the executable**;
4. building the installer, which therefore contains an **already signed** executable;
5. **signing the installer**;
6. the SHA-256 hash and the release.

The order of steps 3 and 4 is not negotiable. Signing only the installer would leave Smart App
Control refusing the application once installed: it inspects the executable at launch, not the
thing that put it there.

Both signing steps skip themselves as long as the `SIGNPATH_PROJECT` variable does not exist.
**The pipeline therefore works today**, producing an unsigned installer — exactly the one built
by hand — which makes it testable before the foundation has answered.

### What will have to be filled in, once the project is accepted

| | Where | What |
|---|---|---|
| `SIGNPATH_API_TOKEN` | repository secret | SignPath API token |
| `SIGNPATH_ORGANIZATION_ID` | repository variable | organisation identifier |
| `SIGNPATH_PROJECT` | repository variable | project slug |

And on the SignPath side, two **artifact configurations** named `app` and `installateur`,
describing respectively the archive holding `Optimus.App.exe` and the bare installer.

## Code signing policy

*This section answers the foundation's requirement. It must stay publicly available.*

**Project.** Optimus — a voice copilot for Star Citizen.
**Repository.** https://github.com/R3coNYT/Optimus-Star-Citizen

**Roles.**

| Role | Who | Responsibility |
|---|---|---|
| Author | R3coN | writes and publishes the code, triggers the builds |
| Reviewer | R3coN | reviews changes before they enter a release |
| Approver | R3coN | approves every signing request in SignPath |

*Single-maintainer project: all three roles are held by the same person, which the foundation
allows. The real control then does not come from separating roles but from the pipeline — only a
tag pushed to the public repository can trigger a signature, and the signed content is
reproducible by anyone from the source.*

**What is signed.** The executable `Optimus.App.exe` and the installer
`Optimus-X.Y.Z-installateur.exe`, both produced by
[`.github/workflows/release.yml`](../.github/workflows/release.yml) on a GitHub runner, from the
code in this repository and nothing else.

**What is never signed.** The third-party components downloaded at install time — the Piper
engine, its voices, whisper.cpp and its model — are neither rebuilt nor signed by this project.
They are verified against a SHA-256 hash declared in the install script, and keep whatever
signature their authors gave them, or its absence.

**Personal data.** None. Optimus runs entirely locally and makes no network request of its own.
Neither the project nor the foundation collects data from the people who install the software.

**Contact.** Through the repository's issues.

## What is left to do, in order

1. ~~Decide on the licence.~~ **GPL-3.0**, on 2026-08-29.
2. ~~Add the `LICENSE` file and link the policy from the README.~~ Done.
3. ~~Make the repository public.~~ Done on 2026-08-29.
4. Confirm that multi-factor authentication is on for the GitHub account. *Maintainer.*
5. Publish a first **unsigned** release through the pipeline, to satisfy “already released in the
   form to be signed” and to put the pipeline to work.
6. **Submit the application** on signpath.org. *Maintainer — I can neither create the account nor
   sign the application on their behalf.* Stated turnaround: a few days to a few weeks.
7. Once accepted: fill in the secret and the two variables, create the artifact configurations,
   re-tag.
8. Check on a third-party machine that SmartScreen stays quiet and Smart App Control lets it
   through.
