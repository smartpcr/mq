# Release Guide

This repository ships releases through the existing GitHub Actions pipeline defined in [`.github/workflows/build.yml`](../.github/workflows/build.yml). Follow the steps below to cut a new release.

## 1. Prepare the release version

1. Ensure all desired changes are merged into the `main` branch.
2. Update [`version.json`](../version.json) with the new semantic version if needed. The project uses [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning), so adjusting the `version` field will automatically cascade to assemblies and NuGet packages.
3. Commit the version bump and push it to `main` (or a dedicated release branch).

## 2. Create a Git tag

1. Create an annotated git tag matching the `v<major>.<minor>.<patch>` format. Tags that match this pattern are watched by the workflow (`on.push.tags: 'v*'`).

   ```bash
   git tag -a v1.0.0 -m "Release v1.0.0"
   git push origin v1.0.0
   ```

2. Pushing the tag triggers the CI workflow on `ubuntu-latest` and `windows-latest` runners. The Release configuration on Ubuntu is responsible for packaging and publishing.

## 3. What the pipeline does

During the run, the workflow will:

1. Restore dependencies and build the solution in both Debug and Release configurations.
2. Detect and execute tests when any project has `<IsTestProject>true</IsTestProject>` or references `Microsoft.NET.Test.Sdk`.
3. For the Ubuntu Release job, pack `MessageQueue.Core` into a NuGet package with `ContinuousIntegrationBuild=true` and upload it as a workflow artifact.
4. Publish the package to the GitHub Packages feed using the `NUGET_API_KEY` secret, skipping duplicates.

See the [workflow file](../.github/workflows/build.yml) for the authoritative definition.

## 4. Verifying the release

1. Navigate to the GitHub Actions run triggered by the tag to monitor build, test, and packaging status.
2. Download the `nuget-packages` artifact from the run if you need to inspect the `.nupkg` locally.
3. Confirm that the package appears under your repository's GitHub Packages tab (or configured NuGet feed).

## 5. Troubleshooting

- **Missing `NUGET_API_KEY` secret** – Add the secret under the repository settings → *Secrets and variables* → *Actions*. Without it, publishing will fail, but the package artifact is still produced.
- **Tests are skipped unexpectedly** – Ensure at least one project sets `<IsTestProject>true</IsTestProject>` or references `Microsoft.NET.Test.Sdk` so the detection step enables testing.
- **Version mismatch** – If the resulting package version is not as expected, verify the values in [`version.json`](../version.json) and that the tag you pushed matches the intended semantic version.

Following these steps will produce a repeatable release through CI without manual packaging.
