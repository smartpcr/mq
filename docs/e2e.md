# End-to-End Scenario Catalog

This document enumerates the end-to-end (E2E) scenarios currently automated in the integration test suite. Each scenario maps back to the system goals and implementation plan defined in the [Persistent Circular Queue design](./design.md) and [implementation plan](./plan.md). Together they validate cross-component behavior spanning the circular buffer, queue manager, handler dispatcher, persistence abstractions, and operational tooling.

## 1. Dead-Letter Queue Lifecycle

These scenarios validate the full lifecycle of messages that exhaust retries or require operator intervention, covering Phase 5 (Retry & Dead-Letter Logic) outcomes in the implementation plan.

| Scenario | Description | Test Coverage |
| --- | --- | --- |
| Max retries transition to DLQ | Drives a message through the retry pipeline until retry budget is exceeded and asserts DLQ placement with failure metadata. | `DeadLetterQueueIntegrationTests.EndToEnd_MaxRetriesExceeded_MovesToDLQ` |
| DLQ replay path | Replays a failed message back to the primary queue with retry counts reset, ensuring consumers can resume processing. | `DeadLetterQueueIntegrationTests.EndToEnd_DLQReplay_ReenqueuesMessage` |
| Lease expiry requeue | Allows a lease to expire and confirms the lease monitor returns the message to the ready queue with incremented retry count. | `DeadLetterQueueIntegrationTests.EndToEnd_LeaseExpiry_RequeulesMessage` |
| Lease expiry → DLQ | Exercises repeated lease expirations until the retry policy moves the message to the DLQ. | `DeadLetterQueueIntegrationTests.EndToEnd_LeaseExpiryWithMaxRetries_MovesToDLQ` |
| DLQ metrics | Aggregates DLQ statistics by type and timestamps to support operational dashboards. | `DeadLetterQueueIntegrationTests.EndToEnd_DLQMetrics_TrackFailurePatterns` |
| DLQ purge (age-based) | Purges dead-letter entries older than a threshold while retaining fresh failures. | `DeadLetterQueueIntegrationTests.EndToEnd_DLQPurge_RemovesOldMessages` |
| Lease extension | Extends an active lease to prevent timeouts for still-running work. | `DeadLetterQueueIntegrationTests.EndToEnd_LeaseExtension_PreventsExpiry` |

## 2. Handler Chaining & Workflow Orchestration

These scenarios demonstrate orchestrated workflows and correlation propagation across chained handlers, aligned with dispatcher objectives from the design doc §4.8.

| Scenario | Description | Test Coverage |
| --- | --- | --- |
| Publisher enqueue | Validates that the queue publisher enqueues messages through the public API surface. | `HandlerChainingTests.QueuePublisher_EnqueuesMessage_Successfully` |
| Correlation propagation | Ensures correlation identifiers assigned by upstream components flow through message metadata. | `HandlerChainingTests.QueuePublisher_PropagatesCorrelationId` |
| Two-step chaining | Simulates a handler producing a follow-up message that inherits correlation context. | `HandlerChainingTests.HandlerChaining_MultipleSteps_PropagatesCorrelation` |
| Deduplication in chained steps | Confirms deduplication semantics when chained handlers enqueue the same logical work item multiple times. | `HandlerChainingTests.HandlerChaining_WithDeduplication_PreventsDuplicates` |
| Three-step order workflow | Executes validation → payment → fulfillment chain with consistent correlation IDs. | `HandlerChainingTests.HandlerChaining_CompleteWorkflow_AllStepsExecute` |

## 3. Long-Running Handler Support & Heartbeats

Heartbeats and lease extension scenarios verify the system’s ability to supervise long-running tasks (§4.7 of the design).

| Scenario | Description | Test Coverage |
| --- | --- | --- |
| Record heartbeat | Persists progress updates emitted by a handler. | `LongRunningHandlerTests.HeartbeatService_RecordsHeartbeat_Successfully` |
| Progressive heartbeats | Tracks successive heartbeat calls and retains the most recent progress state. | `LongRunningHandlerTests.HeartbeatService_MultipleHeartbeats_UpdatesProgress` |
| Heartbeat-driven lease extension | Demonstrates that emitting heartbeats extends leases and avoids premature requeue. | `LongRunningHandlerTests.HeartbeatService_ExtendsLease_PreventTimeout` |
| Heartbeat validation | Guards against invalid progress percentages (negative or >100). | `LongRunningHandlerTests.HeartbeatService_InvalidProgressPercentage_ThrowsException` |
| Last heartbeat timestamp | Exposes the timestamp of the most recent heartbeat for monitoring. | `LongRunningHandlerTests.HeartbeatService_GetLastHeartbeat_ReturnsTimestamp` |
| Completed message guardrail | Prevents heartbeats from being recorded after successful completion. | `LongRunningHandlerTests.HeartbeatService_CompletedMessage_ThrowsException` |
| End-to-end long task | Simulates a multi-step long-running job that repeatedly heartbeats before acknowledgement. | `LongRunningHandlerTests.LongRunningHandler_WithHeartbeats_CompletesSuccessfully` |

## 4. Administrative & Operational Controls

Administrative scenarios exercise the operational surface area described in the design (§4.9) and plan Phase 6.

| Scenario | Description | Test Coverage |
| --- | --- | --- |
| Queue metrics snapshot | Retrieves ready-count and capacity metrics for dashboards. | `AdminOperationsTests.AdminApi_GetMetrics_ReturnsQueueMetrics` |
| Handler metrics baseline | Verifies handler metrics endpoint behavior when no workers are active. | `AdminOperationsTests.AdminApi_GetHandlerMetrics_ReturnsEmptyWhenNoProcessing` |
| Dynamic scaling | Scales handler workers at runtime through the admin API. | `AdminOperationsTests.AdminApi_ScaleHandler_ChangesWorkerCount` |
| Replay dead-letter | Requeues a DLQ message via admin tooling and resets retries. | `AdminOperationsTests.AdminApi_ReplayDeadLetter_RequeulesFailedMessage` |
| Purge DLQ (full) | Clears all DLQ messages on demand. | `AdminOperationsTests.AdminApi_PurgeDeadLetterQueue_RemovesAllMessages` |
| Purge DLQ (age filter) | Removes only entries older than a specified duration. | `AdminOperationsTests.AdminApi_PurgeDeadLetterQueue_WithOlderThan_RemovesOldMessages` |
| Replay missing DLQ entry | Validates error path when replaying a non-existent DLQ message. | `AdminOperationsTests.AdminApi_ReplayDeadLetter_NonExistent_ThrowsException` |
| Snapshot without persistence | Ensures snapshot-triggering fails gracefully when persistence is not configured. | `AdminOperationsTests.AdminApi_WithNoPersistence_TriggerSnapshot_ThrowsException` |
| Replay without DLQ | Validates admin error handling when DLQ support is disabled. | `AdminOperationsTests.AdminApi_WithNoDLQ_ReplayDeadLetter_ThrowsException` |

## 5. Channel-Based Signaling & Auto-Dispatch

These scenarios validate the channel-based notification system and automatic dispatcher signaling introduced to eliminate timer-based polling and manual signal calls.

| Scenario | Description | Test Coverage |
| --- | --- | --- |
| Auto-signaling (unbounded) | Verifies messages are automatically processed without manual `SignalMessageReady()` calls using unbounded channel mode. | `DispatcherAutoSignalingTests.AutoSignaling_UnboundedChannel_ProcessesMessagesWithoutManualSignal` |
| Auto-signaling (bounded coalescing) | Tests bounded coalescing channel mode where signals collapse when workers are busy, ensuring all messages are eventually processed. | `DispatcherAutoSignalingTests.AutoSignaling_BoundedCoalescingChannel_ProcessesMultipleMessages` |
| Mixed message types | Validates independent channel management for different message types with different channel modes (unbounded vs bounded). | `DispatcherAutoSignalingTests.AutoSignaling_MixedMessageTypes_EachTypeProcessedIndependently` |
| Auto-signal with deduplication | Confirms auto-signaling works correctly when messages are replaced via deduplication. | `DispatcherAutoSignalingTests.AutoSignaling_WithDeduplication_AutoSignalsReplacedMessage` |

## 6. Traceability Checklist

To maintain parity between automated coverage and documented flows:

1. Review this catalog when adding new integration tests to ensure every scenario is documented.
2. Update the scenario descriptions if behavior or dependencies change.
3. Cross-reference the design/plan to confirm each architectural pillar retains end-to-end validation.
4. Use the scenario identifiers as building blocks for manual or exploratory testing during release hardening.

## 7. Future Enhancements

- Expand this catalog with stress and recovery scenarios once long-running soak and chaos suites graduate into the main integration pipeline.
- Link each scenario to dashboard views or runbooks as operational tooling matures.
