# MessageQueue Operations Guide

## Table of Contents
1. [Deployment](#deployment)
2. [Configuration](#configuration)
3. [Monitoring](#monitoring)
4. [Troubleshooting](#troubleshooting)
5. [Maintenance](#maintenance)
6. [Disaster Recovery](#disaster-recovery)

## Deployment

### System Requirements
- **.NET Runtime**: .NET 8.0 or .NET Framework 4.6.2+
- **Memory**: Minimum 2GB RAM (4GB+ recommended for high throughput)
- **Disk**: SSD storage recommended for persistence layer
- **CPU**: 2+ cores recommended

### Initial Setup

1. **Install the Package**
```bash
dotnet add package MessageQueue.Host
```

2. **Configure Queue Options**
```csharp
services.Configure<QueueOptions>(options =>
{
    options.Capacity = 100000;  // Adjust based on memory
    options.PersistencePath = "/var/messagequeue/data";
    options.EnablePersistence = true;
    options.EnableDeduplication = true;
    options.SnapshotIntervalSeconds = 60;
    options.DefaultMaxRetries = 5;
});
```

3. **Set File Permissions**
```bash
# Ensure persistence directory exists with correct permissions
mkdir -p /var/messagequeue/data
chmod 700 /var/messagequeue/data
chown appuser:appgroup /var/messagequeue/data
```

## Configuration

### Queue Options

| Option | Default | Description |
|--------|---------|-------------|
| `Capacity` | 10000 | Maximum number of message slots in buffer |
| `PersistencePath` | `./queue-data` | Directory for journal and snapshots |
| `SnapshotIntervalSeconds` | 30 | Time-based snapshot trigger |
| `SnapshotThreshold` | 1000 | Operation count-based snapshot trigger |
| `DefaultTimeout` | 2 minutes | Handler execution timeout |
| `DefaultMaxRetries` | 5 | Max retry attempts before DLQ |
| `DeadLetterQueueCapacity` | 10000 | DLQ capacity |
| `EnablePersistence` | true | Enable/disable persistence |
| `EnableDeduplication` | true | Enable/disable deduplication |

### Handler Options

```csharp
services.Configure<HandlerOptions<MyMessage>>(options =>
{
    options.MaxParallelism = 10;        // Max concurrent handlers
    options.MinParallelism = 2;         // Initial worker count
    options.Timeout = TimeSpan.FromMinutes(5);
    options.MaxRetries = 3;
    options.LeaseDuration = TimeSpan.FromMinutes(5);
    options.BackoffStrategy = RetryBackoffStrategy.Exponential;
    options.InitialBackoff = TimeSpan.FromSeconds(1);
    options.MaxBackoff = TimeSpan.FromMinutes(5);
});
```

### Persistence Options

```csharp
services.Configure<PersistenceOptions>(options =>
{
    options.StoragePath = "/var/messagequeue/data";
    options.JournalFileName = "journal.dat";
    options.SnapshotFileNamePattern = "snapshot-{0:yyyyMMdd-HHmmss}.dat";
    options.SnapshotInterval = TimeSpan.FromSeconds(60);
    options.SnapshotThreshold = 10000;
});
```

## Monitoring

### Key Metrics

1. **Queue Metrics** (via `IQueueAdminApi.GetMetricsAsync()`)
   - `TotalEnqueued`: Lifetime enqueue count
   - `TotalProcessed`: Lifetime processed count
   - `PendingMessages`: Current pending count
   - `InFlightMessages`: Currently being processed
   - `DeadLetterCount`: Messages in DLQ
   - `AverageLatency`: End-to-end processing time

2. **Handler Metrics** (via `IQueueAdminApi.GetHandlerMetricsAsync()`)
   - `ActiveWorkers`: Current worker count
   - `TotalProcessed`: Messages processed by handler
   - `TotalFailed`: Handler failures
   - `AverageProcessingTimeMs`: Handler execution time
   - `MessagesPerSecond`: Throughput

3. **System Health Indicators**
   - **CPU Usage**: <70% under normal load
   - **Memory Usage**: <80% of allocated capacity
   - **Disk I/O**: Monitor journal write latency (<10ms)
   - **Message Latency**: <1 second from enqueue to checkout

### Monitoring Setup

```csharp
// Periodic metrics collection
var metricsTask = Task.Run(async () =>
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var queueMetrics = await adminApi.GetMetricsAsync();
        var handlerMetrics = await adminApi.GetHandlerMetricsAsync();

        // Log or export to monitoring system
        logger.LogInformation(
            "Queue: Pending={Pending}, InFlight={InFlight}, DLQ={DLQ}",
            queueMetrics.PendingMessages,
            queueMetrics.InFlightMessages,
            queueMetrics.DeadLetterCount);

        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
    }
});
```

### Alerts

Set up alerts for:
- **DLQ Growth**: DLQ count increasing >10% in 5 minutes
- **High Latency**: Average latency >5 seconds
- **Low Throughput**: Messages/sec drops >50% from baseline
- **Disk Space**: <10% free space in persistence directory
- **Failed Snapshots**: Snapshot failures
- **Handler Failures**: Failure rate >5%

## Troubleshooting

### Issue: High Message Latency

**Symptoms**: Messages taking >5 seconds from enqueue to processing

**Diagnosis**:
```csharp
var metrics = await adminApi.GetMetricsAsync();
var handlerMetrics = await adminApi.GetHandlerMetricsAsync();

Console.WriteLine($"Pending: {metrics.PendingMessages}");
Console.WriteLine($"InFlight: {metrics.InFlightMessages}");
Console.WriteLine($"Active Workers: {handlerMetrics["MyMessage"].ActiveWorkers}");
Console.WriteLine($"Avg Processing: {handlerMetrics["MyMessage"].AverageProcessingTimeMs}ms");
```

**Resolution**:
1. Scale up handler workers:
```csharp
await adminApi.ScaleHandlerAsync<MyMessage>(20); // Increase to 20 workers
```

2. Check for slow handlers - optimize handler logic
3. Review database/external API call performance in handlers

### Issue: Messages Moving to DLQ

**Symptoms**: DeadLetterCount increasing

**Diagnosis**:
```csharp
var dlqMessages = await deadLetterQueue.GetMessagesAsync(limit: 100);

foreach (var msg in dlqMessages)
{
    Console.WriteLine($"Message: {msg.MessageId}");
    Console.WriteLine($"Failure: {msg.FailureReason}");
    Console.WriteLine($"Exception: {msg.ExceptionMessage}");
    Console.WriteLine($"Retries: {msg.RetryCount}");
}
```

**Resolution**:
1. Review failure reasons - identify patterns
2. Fix underlying handler bugs
3. Replay messages after fix:
```csharp
await adminApi.ReplayDeadLetterAsync(messageId, resetRetryCount: true);
```

### Issue: Persistence Failures

**Symptoms**: Logs showing "Failed to write journal" errors

**Diagnosis**:
```bash
# Check disk space
df -h /var/messagequeue/data

# Check file permissions
ls -la /var/messagequeue/data

# Check file system errors
dmesg | grep -i error
```

**Resolution**:
1. Free up disk space if needed
2. Fix file permissions:
```bash
chmod 700 /var/messagequeue/data
chown appuser:appgroup /var/messagequeue/data/*
```
3. If corruption detected, restore from backup

### Issue: Memory Pressure

**Symptoms**: Out of memory exceptions, GC pressure

**Diagnosis**:
```csharp
var metrics = await adminApi.GetMetricsAsync();
var bufferUsage = (double)metrics.PendingMessages / queueOptions.Capacity * 100;

Console.WriteLine($"Buffer Usage: {bufferUsage:F2}%");
```

**Resolution**:
1. Reduce queue capacity if overprovisioned
2. Increase handler parallelism to drain faster
3. Add more memory to host
4. Implement backpressure on producers

## Maintenance

### Routine Tasks

#### Daily
- Monitor DLQ for patterns
- Review error logs
- Check disk space

#### Weekly
- Review performance metrics trends
- Analyze throughput and latency
- Check for memory leaks (long-running instances)

#### Monthly
- Archive old snapshots
- Review and optimize handler code
- Capacity planning based on growth trends

### Snapshot Management

```bash
# List snapshots
ls -lh /var/messagequeue/data/snapshot-*.dat

# Archive old snapshots (keep last 7 days)
find /var/messagequeue/data -name "snapshot-*.dat" -mtime +7 -exec mv {} /var/messagequeue/archive/ \;

# Manual snapshot trigger
```

```csharp
await adminApi.TriggerSnapshotAsync();
```

### Log Rotation

Configure Serilog file rotation:
```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        "logs/messagequeue-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();
```

## Disaster Recovery

### Backup Strategy

1. **Automated Backups**
```bash
# Daily backup script
#!/bin/bash
BACKUP_DIR="/var/backups/messagequeue/$(date +%Y%m%d)"
DATA_DIR="/var/messagequeue/data"

mkdir -p $BACKUP_DIR
cp $DATA_DIR/journal.dat $BACKUP_DIR/
cp $DATA_DIR/snapshot-*.dat $BACKUP_DIR/
tar -czf $BACKUP_DIR.tar.gz $BACKUP_DIR
rm -rf $BACKUP_DIR
```

2. **Offsite Backup**
```bash
# Upload to S3 or other cloud storage
aws s3 cp /var/backups/messagequeue/*.tar.gz s3://my-bucket/messagequeue-backups/
```

### Recovery Procedures

#### Full Recovery from Backup

1. Stop the queue service
2. Restore data files:
```bash
tar -xzf /var/backups/messagequeue/20250106.tar.gz -C /var/messagequeue/data
```

3. Start the queue service - it will automatically replay journal

#### Partial Data Loss

If only journal is lost:
1. Latest snapshot will be loaded
2. Messages since last snapshot are lost
3. Monitor DLQ for duplicate detection

### Testing Recovery

Periodically test recovery:
```bash
# 1. Create test backup
cp -r /var/messagequeue/data /var/messagequeue/data.backup

# 2. Simulate failure
rm /var/messagequeue/data/journal.dat

# 3. Verify recovery on startup
# Check logs for "Recovery completed: MessagesRestored=X"

# 4. Restore original if test fails
rm -rf /var/messagequeue/data
mv /var/messagequeue/data.backup /var/messagequeue/data
```

## Performance Tuning

### High Throughput Configuration

```csharp
services.Configure<QueueOptions>(options =>
{
    options.Capacity = 500000;  // Large buffer
    options.SnapshotIntervalSeconds = 300;  // Less frequent snapshots
    options.SnapshotThreshold = 100000;
});

services.Configure<HandlerOptions<MyMessage>>(options =>
{
    options.MaxParallelism = 50;  // Many workers
    options.MinParallelism = 10;
});
```

### Low Latency Configuration

```csharp
services.Configure<QueueOptions>(options =>
{
    options.Capacity = 10000;  // Smaller buffer
    options.SnapshotIntervalSeconds = 30;  // Frequent snapshots
    options.DefaultTimeout = TimeSpan.FromSeconds(30);  // Shorter timeout
});

services.Configure<HandlerOptions<MyMessage>>(options =>
{
    options.MaxParallelism = 20;
    options.MinParallelism = 5;
    options.BackoffStrategy = RetryBackoffStrategy.Fixed;  // Faster retries
    options.InitialBackoff = TimeSpan.FromMilliseconds(100);
});
```

## Security Considerations

1. **File Permissions**: Ensure persistence directory is only readable by application user
2. **Sensitive Data**: Consider encryption for message payloads containing PII
3. **Access Control**: Protect admin APIs with authentication/authorization
4. **Audit Logging**: Enable detailed logging for compliance requirements

## Support

For issues not covered in this guide:
- GitHub Issues: https://github.com/your-org/messagequeue/issues
- Documentation: https://docs.your-org.com/messagequeue
- Support Email: support@your-org.com
