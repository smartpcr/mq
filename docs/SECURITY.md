# MessageQueue Security Review

## Summary

This document outlines the security considerations and best practices for the MessageQueue system.

## Security Assessment

### ✅ Strengths

1. **No Network Exposure**: In-memory queue with file-based persistence only
2. **Type Safety**: Strong typing with compile-time checks
3. **Dependency Injection**: Secure service resolution with scoped lifetimes
4. **Error Isolation**: Handler failures don't cascade to queue infrastructure
5. **Audit Trail**: Journal provides complete operation history

### ⚠️ Areas Requiring Attention

1. **File Permissions**: Persistence files require proper access control
2. **Sensitive Data**: No built-in encryption for message payloads
3. **Admin API Access**: No authentication/authorization built-in
4. **Resource Limits**: No built-in DoS protection
5. **Input Validation**: Handler responsibility

## Threat Model

### Threats Addressed

| Threat | Mitigation | Status |
|--------|------------|--------|
| **Unauthorized file access** | File permission checks, OS-level security | ✅ Implemented |
| **Handler exceptions crashing queue** | Exception isolation, try-catch boundaries | ✅ Implemented |
| **Infinite retry loops** | MaxRetries configuration, DLQ | ✅ Implemented |
| **Memory exhaustion** | Fixed circular buffer capacity | ✅ Implemented |
| **Journal corruption** | CRC checksums, validation on read | ✅ Implemented |
| **Race conditions** | Lock-free CAS operations, atomic updates | ✅ Implemented |

### Threats Requiring User Action

| Threat | Recommended Mitigation | User Responsibility |
|--------|------------------------|---------------------|
| **Sensitive data in persistence files** | Encrypt message payloads before enqueue | ✅ Application |
| **Unauthorized admin API access** | Add authentication middleware | ✅ Application |
| **Malicious message payloads** | Validate in handlers, sanitize inputs | ✅ Handler Code |
| **DoS via message flooding** | Implement rate limiting on publishers | ✅ Application |
| **File system tampering** | Use OS file permissions, SELinux/AppArmor | ✅ Infrastructure |

## Security Recommendations

### 1. File System Security

**Issue**: Persistence files contain message data and may include sensitive information.

**Recommendation**:
```bash
# Set restrictive permissions on data directory
chmod 700 /var/messagequeue/data
chown appuser:appgroup /var/messagequeue/data

# Ensure journal and snapshots are not world-readable
chmod 600 /var/messagequeue/data/*.dat
```

**Code Check**:
```csharp
// Verify permissions on startup
var dataPath = "/var/messagequeue/data";
var dirInfo = new DirectoryInfo(dataPath);

if ((dirInfo.Attributes & FileAttributes.ReadOnly) == 0)
{
    // Check Unix permissions if on Linux
    var unixFileInfo = new UnixFileInfo(dataPath);
    if ((unixFileInfo.FileAccessPermissions & UnixFileAccessPermissions.OtherReadWriteExecute) != 0)
    {
        throw new SecurityException("Data directory has insecure permissions");
    }
}
```

### 2. Message Payload Encryption

**Issue**: Message payloads are stored in plain text in persistence files.

**Recommendation**: Implement application-level encryption for sensitive data.

**Example**:
```csharp
public class EncryptedMessage
{
    public string EncryptedPayload { get; set; } // AES encrypted
    public byte[] IV { get; set; }
}

public class SecureMessageHandler : IMessageHandler<EncryptedMessage>
{
    private readonly IEncryptionService _encryption;

    public async Task HandleAsync(EncryptedMessage message, CancellationToken cancellationToken)
    {
        // Decrypt payload
        var plaintext = await _encryption.DecryptAsync(message.EncryptedPayload, message.IV);

        // Process decrypted data
        var actualMessage = JsonSerializer.Deserialize<SensitiveData>(plaintext);

        // ... process message
    }
}

// Publisher side
public async Task PublishSensitiveData(SensitiveData data)
{
    var plaintext = JsonSerializer.Serialize(data);
    var (encrypted, iv) = await _encryption.EncryptAsync(plaintext);

    await _publisher.PublishAsync(new EncryptedMessage
    {
        EncryptedPayload = encrypted,
        IV = iv
    });
}
```

### 3. Admin API Security

**Issue**: Admin APIs have no built-in authentication.

**Recommendation**: Wrap with ASP.NET Core authorization.

**Example**:
```csharp
// Startup configuration
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("QueueAdmin", policy =>
        policy.RequireRole("Administrator"));
});

// Controller
[Authorize(Policy = "QueueAdmin")]
[ApiController]
[Route("api/admin/queue")]
public class QueueAdminController : ControllerBase
{
    private readonly IQueueAdminApi _adminApi;

    public QueueAdminController(IQueueAdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        var metrics = await _adminApi.GetMetricsAsync();
        return Ok(metrics);
    }

    [HttpPost("scale/{messageType}/{count}")]
    public async Task<IActionResult> ScaleHandler(string messageType, int count)
    {
        // Additional validation
        if (count < 1 || count > 100)
        {
            return BadRequest("Count must be between 1 and 100");
        }

        // Scale handler (use reflection to call generic method)
        var type = Type.GetType(messageType);
        if (type == null)
        {
            return NotFound("Message type not found");
        }

        var method = typeof(IQueueAdminApi).GetMethod(nameof(IQueueAdminApi.ScaleHandlerAsync));
        var genericMethod = method.MakeGenericMethod(type);
        await (Task)genericMethod.Invoke(_adminApi, new object[] { count, CancellationToken.None });

        return Ok();
    }
}
```

### 4. Input Validation

**Issue**: Handlers receive deserialized objects that may contain malicious data.

**Recommendation**: Validate all message fields in handlers.

**Example**:
```csharp
public class OrderHandler : IMessageHandler<OrderMessage>
{
    public async Task HandleAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(message.OrderId))
        {
            throw new ValidationException("OrderId is required");
        }

        if (message.OrderId.Length > 50)
        {
            throw new ValidationException("OrderId too long");
        }

        if (!Regex.IsMatch(message.OrderId, @"^[A-Z0-9\-]+$"))
        {
            throw new ValidationException("OrderId contains invalid characters");
        }

        if (message.Amount < 0 || message.Amount > 1000000)
        {
            throw new ValidationException("Amount out of valid range");
        }

        // Process validated message
        await ProcessOrder(message);
    }
}
```

### 5. Rate Limiting

**Issue**: No built-in protection against message flooding.

**Recommendation**: Implement rate limiting at the publisher level.

**Example**:
```csharp
public class RateLimitedPublisher : IQueuePublisher
{
    private readonly IQueuePublisher _innerPublisher;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxMessagesPerSecond;
    private DateTime _lastReset = DateTime.UtcNow;
    private int _messageCount = 0;

    public RateLimitedPublisher(IQueuePublisher innerPublisher, int maxMessagesPerSecond = 1000)
    {
        _innerPublisher = innerPublisher;
        _maxMessagesPerSecond = maxMessagesPerSecond;
        _semaphore = new SemaphoreSlim(1, 1);
    }

    public async Task<Guid> PublishAsync<TMessage>(
        TMessage message,
        string? deduplicationKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastReset > TimeSpan.FromSeconds(1))
            {
                _messageCount = 0;
                _lastReset = now;
            }

            if (_messageCount >= _maxMessagesPerSecond)
            {
                throw new RateLimitExceededException(
                    $"Rate limit of {_maxMessagesPerSecond} messages/sec exceeded");
            }

            _messageCount++;

            return await _innerPublisher.PublishAsync(
                message,
                deduplicationKey,
                correlationId,
                cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

### 6. Resource Limits

**Recommendation**: Configure appropriate limits based on available resources.

```csharp
services.Configure<QueueOptions>(options =>
{
    // Limit buffer size based on available memory
    // Rule of thumb: 1KB per message * capacity should be < 50% available RAM
    options.Capacity = 100000; // ~100MB for 1KB messages

    // Limit DLQ to prevent unbounded growth
    options.DeadLetterQueueCapacity = 10000;

    // Set reasonable timeouts
    options.DefaultTimeout = TimeSpan.FromMinutes(5); // Not too long

    // Limit retries
    options.DefaultMaxRetries = 5; // Prevent infinite loops
});
```

### 7. Error Information Disclosure

**Issue**: Exception details in DLQ might leak sensitive information.

**Recommendation**: Sanitize exception messages before storing.

**Example**:
```csharp
public class SecureDeadLetterQueue : IDeadLetterQueue
{
    private readonly IDeadLetterQueue _innerDlq;

    public Task AddAsync(
        MessageEnvelope envelope,
        string failureReason,
        Exception exception = null,
        CancellationToken cancellationToken = default)
    {
        // Sanitize exception details
        var sanitizedException = exception != null
            ? new Exception($"Handler failed: {exception.GetType().Name}")
            : null;

        // Sanitize failure reason - remove any potential PII
        var sanitizedReason = SanitizeFailureReason(failureReason);

        return _innerDlq.AddAsync(envelope, sanitizedReason, sanitizedException, cancellationToken);
    }

    private string SanitizeFailureReason(string reason)
    {
        // Remove email addresses
        reason = Regex.Replace(reason, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "[EMAIL]");

        // Remove phone numbers
        reason = Regex.Replace(reason, @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", "[PHONE]");

        // Remove credit card numbers
        reason = Regex.Replace(reason, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", "[CARD]");

        return reason;
    }
}
```

## Security Checklist

Before deploying to production:

- [ ] Set file permissions to 700 for data directory
- [ ] Set file permissions to 600 for journal and snapshot files
- [ ] Implement encryption for sensitive message payloads
- [ ] Add authentication/authorization to admin APIs
- [ ] Implement rate limiting on message publishers
- [ ] Add input validation to all message handlers
- [ ] Configure appropriate resource limits (capacity, timeouts, retries)
- [ ] Review error messages for information disclosure
- [ ] Set up monitoring and alerting
- [ ] Document security requirements for operators
- [ ] Conduct penetration testing (if applicable)
- [ ] Review handler code for SQL injection, command injection, etc.
- [ ] Implement audit logging for admin operations
- [ ] Use HTTPS for any network APIs
- [ ] Keep dependencies up to date (security patches)

## Compliance Considerations

### GDPR / Data Privacy

- **Personal Data**: If messages contain personal data, implement:
  - Encryption at rest (message payloads)
  - Right to erasure (ability to purge specific messages from DLQ)
  - Data retention policies (automatic DLQ purging)
  - Audit logging (journal already provides this)

### PCI-DSS

- **Cardholder Data**: If processing payment information:
  - Encrypt message payloads containing card data
  - Restrict access to persistence files
  - Implement key management for encryption
  - Mask card numbers in logs and DLQ

### HIPAA

- **Protected Health Information**: If processing medical data:
  - Encrypt all message payloads
  - Implement access controls on admin APIs
  - Enable audit logging for all operations
  - Ensure proper disposal of persistence files

## Reporting Security Issues

To report a security vulnerability:

1. **Do not** open a public GitHub issue
2. Email: security@your-org.com
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if available)

We aim to respond within 48 hours and provide a fix within 30 days for critical issues.

## Security Updates

Subscribe to security advisories:
- GitHub Security Advisories
- NuGet package security alerts
- Mailing list: security-announce@your-org.com

## Conclusion

The MessageQueue system provides a solid foundation for secure message processing, but security is a shared responsibility between the library and the application using it. Follow the recommendations in this document to ensure a secure deployment.
