# Getting Started with Message Queue

This guide helps developers quickly get started with the Message Queue project.

## Prerequisites

- .NET SDK 8.0 or later (supports both .NET 8.0 and .NET Framework 4.6.2)
- Visual Studio 2022, VS Code, or Rider (optional)
- Git

## Quick Start

### 1. Clone and Build

```bash
git clone <repository-url>
cd mq

# Restore and build the solution
dotnet build MessageQueue.sln
```

### 2. Verify Build

```bash
# Should complete with 0 errors
dotnet build MessageQueue.sln

# Run tests (currently empty, to be implemented)
dotnet test MessageQueue.sln
```

### 3. Explore the Structure

```
MessageQueue/
├── src/                    # 7 source projects
├── tests/                  # 6 test projects
├── samples/                # 2 sample projects
├── docs/                   # Documentation
└── MessageQueue.sln        # Solution file (15 projects)
```

## Development Workflow

### For New Developers

1. **Read the documentation**:
   - [Design Document](design.md) - Understand the architecture
   - [Implementation Plan](plan.md) - See the roadmap
   - [Project Structure](project-structure.md) - Navigate the codebase

2. **Choose your work stream** (see Implementation Plan):
   - Core Stream: Circular buffer and deduplication
   - Persistence Stream: Journal, snapshot, recovery
   - Dispatcher Stream: Handler execution infrastructure
   - Infrastructure Stream: Admin APIs and monitoring

3. **Set up your branch**:
   ```bash
   git checkout -b feature/your-component-name
   ```

4. **Start coding**:
   - Phase 1: Define interfaces in `MessageQueue.Core/Interfaces/`
   - Follow naming conventions and coding standards
   - Write tests alongside implementation

### For Team Leads

1. **Assign developers to streams** based on the implementation plan
2. **Set up integration points** at the milestones:
   - Day 15: Core components integration
   - Day 28: Dispatcher integration
   - Day 42: Full system integration

3. **Review merge strategy**:
   - Feature branches → `develop` → `main`
   - Require integration tests to pass before merging

## Project Organization

### Source Projects (src/)

| Project | Purpose | Dependencies |
|---------|---------|--------------|
| MessageQueue.Core | Interfaces and models | None |
| MessageQueue.CircularBuffer | Lock-free buffer | Core |
| MessageQueue.Persistence | Journal/snapshot | Core |
| MessageQueue.Dispatcher | Handler execution | Core, CircularBuffer |
| MessageQueue.DeadLetter | Failed message handling | Core, CircularBuffer |
| MessageQueue.Admin | Admin APIs | Core |
| MessageQueue.Host | Integrated host | All above |

### Test Projects (tests/)

| Project | Type | Purpose |
|---------|------|---------|
| MessageQueue.Core.Tests | Unit | Contract validation, serialization |
| MessageQueue.CircularBuffer.Tests | Unit + Integration | Buffer operations, stress tests |
| MessageQueue.Persistence.Tests | Unit + Integration | Journal, snapshot, recovery |
| MessageQueue.Dispatcher.Tests | Unit + Integration | Handler dispatch, workers |
| MessageQueue.Integration.Tests | End-to-end | Complete workflows |
| MessageQueue.Performance.Tests | Benchmarks | BenchmarkDotNet tests |

### Sample Projects (samples/)

| Project | Purpose |
|---------|---------|
| MessageQueue.Samples.Basic | Basic usage examples |
| MessageQueue.Samples.Advanced | Advanced features demo |

## Common Commands

### Building

```bash
# Build everything
dotnet build MessageQueue.sln

# Build specific project
dotnet build src/MessageQueue.Core/MessageQueue.Core.csproj

# Release build
dotnet build MessageQueue.sln -c Release

# Build for specific framework
dotnet build -f net8.0
```

### Testing

```bash
# Run all tests
dotnet test MessageQueue.sln

# Run specific test project
dotnet test tests/MessageQueue.Core.Tests/MessageQueue.Core.Tests.csproj

# Run with coverage
dotnet test /p:CollectCoverage=true

# Filter specific test
dotnet test --filter "FullyQualifiedName~CircularBufferTests"
```

### Running Samples

```bash
# Basic sample
dotnet run --project samples/MessageQueue.Samples.Basic/MessageQueue.Samples.Basic.csproj

# Advanced sample
dotnet run --project samples/MessageQueue.Samples.Advanced/MessageQueue.Samples.Advanced.csproj
```

### Performance Testing

```bash
# Run benchmarks
dotnet run --project tests/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release
```

## Phase 1: Getting Started (Week 1)

### Day 1-3: Define Contracts

All developers collaborate on interface definitions:

1. **Open** `src/MessageQueue.Core/Interfaces/`
2. **Create** interface files:
   - `IQueueManager.cs`
   - `ICircularBuffer.cs`
   - `IPersister.cs`
   - `IHandlerDispatcher.cs`
   - `ILeaseMonitor.cs`
   - `IMessageHandler.cs`
   - `IQueuePublisher.cs`
   - `IDeadLetterQueue.cs`

3. **Define** shared models in `src/MessageQueue.Core/Models/`:
   - `MessageEnvelope.cs`
   - `DeadLetterEnvelope.cs`
   - `MessageMetadata.cs`

4. **Create** enums in `src/MessageQueue.Core/Enums/`:
   - `MessageStatus.cs`
   - `OperationCode.cs`

5. **Implement** options in `src/MessageQueue.Core/Options/`:
   - `QueueOptions.cs`
   - `HandlerOptions.cs`

### Day 4-7: Tests and Infrastructure

1. **Write unit tests** in `tests/MessageQueue.Core.Tests/`:
   - Model serialization tests
   - Configuration validation
   - DI registration tests

2. **Create test utilities** in `tests/MessageQueue.Core.Tests/`:
   - Mock message builders
   - Test fixtures
   - Helper extensions

## Coding Standards

### Naming Conventions

- Interfaces: `IInterfaceName`
- Classes: `PascalCase`
- Methods: `PascalCase`
- Private fields: `_camelCase`
- Parameters: `camelCase`
- Test methods: `MethodName_Scenario_ExpectedBehavior`

### Code Organization

```csharp
namespace MessageQueue.ComponentName;

/// <summary>
/// XML documentation for public APIs
/// </summary>
public class ClassName
{
    private readonly IDependency _dependency;

    public ClassName(IDependency dependency)
    {
        _dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));
    }

    public void PublicMethod()
    {
        // Implementation
    }
}
```

### Testing Conventions

```csharp
namespace MessageQueue.ComponentName.Tests;

[TestClass]
public class ClassNameTests
{
    [TestMethod]
    public void MethodName_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        var sut = new ClassName();

        // Act
        var result = sut.MethodName();

        // Assert
        result.Should().NotBeNull();
    }
}
```

## Next Steps

1. **Join your team's work stream** (see Implementation Plan)
2. **Review the design document** to understand your component
3. **Start with Phase 1 tasks** in your assigned area
4. **Write tests first** for TDD approach
5. **Submit PRs early** for quick feedback

## Getting Help

- **Design questions**: See `docs/design.md`
- **Implementation questions**: See `docs/plan.md`
- **Structure questions**: See `docs/project-structure.md`
- **Code guidance**: See `CLAUDE.md` for AI assistant context

## Resources

- [.NET 8.0 Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [BenchmarkDotNet](https://benchmarkdotnet.org/)
- [MSTest Framework](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest)
- [FluentAssertions](https://fluentassertions.com/)

---

**Ready to start? Pick up a task from the Implementation Plan and begin coding!**
