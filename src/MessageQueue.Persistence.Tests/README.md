# MessageQueue.Persistence.Tests

## Purpose

Unit and integration tests for journal, snapshot, and crash recovery mechanisms.

## Test Categories

- **Unit/** - Component-level tests
  - Journal record encoding/decoding
  - CRC validation logic
  - Snapshot serialization correctness
  - Trigger evaluation (time/threshold)
  - File I/O error handling

- **Integration/** - Recovery scenarios
  - Crash recovery with journal replay
  - Corrupt file handling
  - Partial replay scenarios
  - Graceful shutdown with final snapshot
  - DLQ persistence cycle

## Key Test Scenarios

- Write-replay consistency validation
- Journal truncation safety
- Snapshot completeness verification
- File integrity under concurrent writes
- Recovery from various failure points

## Running Tests

```bash
# All tests
dotnet test src/MessageQueue.Persistence.Tests/MessageQueue.Persistence.Tests.csproj

# Recovery tests
dotnet test --filter "FullyQualifiedName~Recovery"
```

## Phase

**Phase 3** - Persistence & Recovery (Weeks 3-4, Days 15-28)
