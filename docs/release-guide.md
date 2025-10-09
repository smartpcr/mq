# Release Guide

The repository ships releases through two GitHub Actions workflows:

- [`build.yml`](../.github/workflows/build.yml) — runs automatically on every push, pull request, and version tag to validate builds and publishes packages from CI.
- [`manual-release.yml`](../.github/workflows/manual-release.yml) — a manually triggered workflow that builds, packages, and creates the official GitHub release (including publishing to GitHub Packages when credentials are configured).

Use the manual workflow whenever you need to cut a new release. The automated build continues to provide continuous validation and package publishing for tagged builds, but the manual workflow is the canonical release path.

## 1. Prepare the release version

1. Ensure all desired changes are merged into the `main` branch.
2. Update [`version.json`](../version.json) with the target semantic version if required. The project uses [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning), so adjusting the `version` field cascades to assemblies and NuGet packages.
3. Commit the version bump to `main` (or a dedicated release branch) so the workflow can build from the correct revision.

## 2. Trigger the manual release workflow

1. Navigate to **Actions → Manual Release** in the repository UI.
2. Click **Run workflow** and choose the branch or tag to release (default is `main`).
3. Provide the workflow inputs:
   - **Release version** – The semantic version to publish (e.g., `1.0.0`). Leave this blank to use the version calculated by Nerdbank.GitVersioning. A leading `v` is optional; the workflow normalises it and creates a `v<version>` tag automatically.
   - **Release notes** – Optional Markdown that becomes the GitHub release body. Leave blank to use the default message.
4. Confirm the run. The workflow executes on an Ubuntu runner and handles the full release lifecycle.

## 3. What the manual workflow does

During the run, the `release` job will:

1. Restore dependencies and build the entire solution in `Release` configuration.
2. Execute the solution's test suite in `Release` mode.
3. Pack `MessageQueue.Core` into a `.nupkg`, forcing `ContinuousIntegrationBuild=true` and the version supplied via workflow input.
4. Upload the package as a workflow artifact for later retrieval.
5. Publish the package to the repository's GitHub Packages feed when the `NUGET_API_KEY` secret is configured (publishing is skipped otherwise, but the release still completes).
6. Create a GitHub release with a tag named `v<version>`, attach the generated `.nupkg`, and populate the release notes.

Refer to the [workflow definition](../.github/workflows/manual-release.yml) for the authoritative configuration.

## 4. Verifying the release

1. Monitor the Actions run to ensure build, test, packaging, and release creation steps succeed.
2. Download the `messagequeue-nuget-<version>` artifact from the run if you need to inspect the `.nupkg` locally.
3. Confirm a new GitHub release appears under the repository's **Releases** tab with the expected assets and notes.
4. If package publishing is enabled, verify the package is present on the repository's GitHub Packages feed (or any additional feeds the workflow targets).

## 5. Troubleshooting

- **Invalid version input** – The workflow strips a leading `v`, but blank or malformed versions fail fast. Re-run with a valid semantic version (e.g., `1.2.3`).
- **Missing `NUGET_API_KEY` secret** – Publishing to GitHub Packages requires the secret under *Settings → Secrets and variables → Actions*. Without it, the workflow skips the publish step but still creates the release.
- **Release creation failed** – Ensure the workflow's `GITHUB_TOKEN` has permissions to create releases (default is sufficient). Re-run after resolving any branch protection or permission issues.
- **Version mismatch** – Confirm the workflow input matches the desired version and that [`version.json`](../version.json) reflects the same value. The workflow stamps the `.nupkg` with the supplied version.

Following these steps yields a repeatable, one-click release process backed by CI.
