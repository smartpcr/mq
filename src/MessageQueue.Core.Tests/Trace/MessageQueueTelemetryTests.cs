// -----------------------------------------------------------------------
// <copyright file="MessageQueueTelemetryTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests.Trace;

using System;
using FluentAssertions;
using MessageQueue.Core.Trace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MessageQueueTelemetryTests
{
    [TestMethod]
    public void Constructor_WithNone_CreatesInstance()
    {
        // Act
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.None);

        // Assert
        telemetry.Should().NotBeNull();
    }

    [TestMethod]
    public void Constructor_WithETW_CreatesInstance()
    {
        // Act
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.ETW);

        // Assert
        telemetry.Should().NotBeNull();
    }

    [TestMethod]
    public void Constructor_WithOpenTelemetry_CreatesInstance()
    {
        // Act
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.OpenTelemetry);

        // Assert
        telemetry.Should().NotBeNull();
    }

    [TestMethod]
    public void Constructor_WithAll_CreatesInstance()
    {
        // Act
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);

        // Assert
        telemetry.Should().NotBeNull();
    }

    [TestMethod]
    public void MessageEnqueued_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);
        var messageId = Guid.NewGuid();

        // Act
        Action act = () => telemetry.MessageEnqueued(messageId, "TestMessage", "dedup-key");

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void MessageCheckedOut_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);
        var messageId = Guid.NewGuid();

        // Act
        Action act = () => telemetry.MessageCheckedOut(messageId, "handler-1", TimeSpan.FromMinutes(5));

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void HandlerCompleted_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);
        var messageId = Guid.NewGuid();

        // Act
        Action act = () => telemetry.HandlerCompleted(messageId, "TestMessage", TimeSpan.FromMilliseconds(100));

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void RecordMessageEnqueued_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);

        // Act
        Action act = () => telemetry.RecordMessageEnqueued("TestMessage");

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void RecordHandlerDuration_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);

        // Act
        Action act = () => telemetry.RecordHandlerDuration("TestMessage", TimeSpan.FromMilliseconds(100), true);

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void RecordQueueDepth_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);

        // Act
        Action act = () => telemetry.RecordQueueDepth(10, 5, 2);

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Error_WithException_DoesNotThrow()
    {
        // Arrange
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);
        var exception = new InvalidOperationException("Test error");

        // Act
        Action act = () => telemetry.Error("TestOperation", "Error occurred", exception);

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var telemetry = new MessageQueueTelemetry(TelemetryProvider.All);

        // Act
        Action act = () =>
        {
            telemetry.Dispose();
            telemetry.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [TestMethod]
    public void TelemetryProvider_None_DoesNotCreateInstances()
    {
        // Arrange & Act
        using var telemetry = new MessageQueueTelemetry(TelemetryProvider.None);

        // Assert - should not throw and should complete instantly
        telemetry.MessageEnqueued(Guid.NewGuid(), "Test", null);
        telemetry.RecordMessageEnqueued("Test");
    }
}
