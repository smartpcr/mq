# GitHub Packages Publishing

The MessageQueue.Core NuGet package is automatically published to GitHub Packages when code is pushed to the main branch or version tags are created.

## Workflow Configuration

The GitHub Actions workflow (`.github/workflows/build.yml`) is configured to:

### Build Matrix

Builds are run across multiple environments to ensure compatibility:
- **Operating Systems**: ubuntu-latest, windows-latest, macos-latest
- **Configurations**: Debug, Release

This creates 6 build combinations (3 OS × 2 configurations).

### Package Publishing

Packages are only created and published under specific conditions:

**Pack Condition:**
```yaml
if: matrix.configuration == 'Release' && matrix.os == 'ubuntu-latest'
```
- Only runs for **Release** configuration
- Only runs on **ubuntu-latest** runner
- Output directory: `./packages`

**Publish Condition:**
```yaml
if: matrix.configuration == 'Release' && matrix.os == 'ubuntu-latest' &&
    github.event_name == 'push' &&
    (github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/tags/v'))
```
- Only runs for **Release** configuration
- Only runs on **ubuntu-latest** runner
- Only runs on **push** events (not pull requests)
- Only publishes from:
  - `main` branch pushes
  - Version tag pushes (e.g., `v1.0.0`, `v2.1.3`)

**Publish Target:**
- **GitHub Packages** (not nuget.org)
- Source: `https://nuget.pkg.github.com/{owner}/index.json`
- Authentication: Uses `GITHUB_TOKEN` (automatically provided)

## Package Output

Packages are output to the `./packages` directory:
```bash
dotnet pack src/MessageQueue.Core/MessageQueue.Core.csproj \
  --configuration Release \
  --no-build \
  --output ./packages \
  -p:ContinuousIntegrationBuild=true
```

The `ContinuousIntegrationBuild=true` property ensures deterministic builds for proper source linking.

## Consuming Packages from GitHub

To use packages published to GitHub Packages, add a `nuget.config` file to your project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="github" value="https://nuget.pkg.github.com/{OWNER}/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="{GITHUB_USERNAME}" />
      <add key="ClearTextPassword" value="{GITHUB_TOKEN}" />
    </github>
  </packageSourceCredentials>
</configuration>
```

Replace:
- `{OWNER}` - GitHub organization or username
- `{GITHUB_USERNAME}` - Your GitHub username
- `{GITHUB_TOKEN}` - GitHub Personal Access Token with `read:packages` scope

## Manual Package Publishing

To manually publish a package to GitHub Packages:

```bash
# Build and pack
dotnet pack src/MessageQueue.Core/MessageQueue.Core.csproj \
  --configuration Release \
  --output ./packages

# Publish to GitHub Packages
dotnet nuget push ./packages/*.nupkg \
  --api-key $GITHUB_TOKEN \
  --source https://nuget.pkg.github.com/{OWNER}/index.json \
  --skip-duplicate
```

## Versioning

Package versions are controlled by:
1. **Version tags**: When you push a tag like `v1.2.3`, the package version will be `1.2.3`
2. **Nerdbank.GitVersioning**: Automatic version calculation based on git history (if configured)
3. **Manual version**: Set in `MessageQueue.Core.csproj` `<Version>` property

## Workflow Triggers

The workflow runs on:
- **Push to main branch** - Builds, tests, and publishes package
- **Version tag push** (`v*`) - Builds, tests, and publishes package with tag version
- **Pull requests** - Builds and tests only (no publishing)

## Artifacts

Build artifacts are uploaded for the Release/ubuntu-latest configuration:
- **Name**: `nuget-packages`
- **Path**: `packages/*.nupkg`
- **Retention**: Default (90 days)

These artifacts can be downloaded from the GitHub Actions run page for inspection or manual deployment.

## Troubleshooting

### Package Not Publishing

Check that:
1. Build configuration is `Release`
2. Runner OS is `ubuntu-latest`
3. Event is a push (not pull request)
4. Branch is `main` or tag starts with `v`
5. Tests are passing
6. `GITHUB_TOKEN` has packages:write permission

### Authentication Errors

GitHub Actions automatically provides `GITHUB_TOKEN` with appropriate permissions. If you see authentication errors:
1. Check repository settings → Actions → General → Workflow permissions
2. Ensure "Read and write permissions" is selected
3. Enable "Allow GitHub Actions to create and approve pull requests" if needed

### Package Already Exists

The `--skip-duplicate` flag prevents errors when the same version already exists. To publish a new version:
1. Update the version in `MessageQueue.Core.csproj`, or
2. Push a new version tag (e.g., `v1.0.1`)

## See Also

- [GitHub Packages Documentation](https://docs.github.com/en/packages)
- [NuGet Package Management](https://learn.microsoft.com/en-us/nuget/)
- [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)
