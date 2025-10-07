// -----------------------------------------------------------------------
// <copyright file="HandlerRegistryTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests;

using System;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class HandlerRegistryTests
{
    private IServiceProvider serviceProvider = null!;
    private HandlerRegistry registry = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddTransient<TestMessageHandler>();
        services.AddTransient<AnotherTestMessageHandler>();
        this.serviceProvider = services.BuildServiceProvider();
        this.registry = new HandlerRegistry(this.serviceProvider);
    }

    [TestCleanup]
    public void Cleanup()
    {
        (this.serviceProvider as IDisposable)?.Dispose();
    }

    [TestMethod]
    public void RegisterHandler_WithTypeParameters_RegistersSuccessfully()
    {
        // Act
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();

        // Assert
        var isRegistered = this.registry.IsRegistered(typeof(TestMessage));
        isRegistered.Should().BeTrue();
    }

    [TestMethod]
    public void RegisterHandler_WithOptions_StoresOptions()
    {
        // Arrange
        var options = new HandlerOptions<TestMessage>
        {
            MinParallelism = 2,
            MaxParallelism = 10,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        // Act
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>(options);

        // Assert
        var registration = this.registry.GetRegistration(typeof(TestMessage));
        registration.Should().NotBeNull();
        registration.Options.Should().Be(options);
    }

    [TestMethod]
    public void RegisterHandler_WithFactory_RegistersSuccessfully()
    {
        // Arrange
        Func<IServiceProvider, IMessageHandler<TestMessage>> factory =
            sp => new TestMessageHandler();

        // Act
        this.registry.RegisterHandler(factory);

        // Assert
        var isRegistered = this.registry.IsRegistered(typeof(TestMessage));
        isRegistered.Should().BeTrue();
    }

    [TestMethod]
    public void GetRegistration_WhenRegistered_ReturnsRegistration()
    {
        // Arrange
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();

        // Act
        var registration = this.registry.GetRegistration(typeof(TestMessage));

        // Assert
        registration.Should().NotBeNull();
        registration.MessageType.Should().Be(typeof(TestMessage));
        registration.HandlerType.Should().Be(typeof(TestMessageHandler));
    }

    [TestMethod]
    public void GetRegistration_WhenNotRegistered_ReturnsNull()
    {
        // Act
        var registration = this.registry.GetRegistration(typeof(TestMessage));

        // Assert
        registration.Should().BeNull();
    }

    [TestMethod]
    public void IsRegistered_WhenRegistered_ReturnsTrue()
    {
        // Arrange
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();

        // Act
        var isRegistered = this.registry.IsRegistered(typeof(TestMessage));

        // Assert
        isRegistered.Should().BeTrue();
    }

    [TestMethod]
    public void IsRegistered_WhenNotRegistered_ReturnsFalse()
    {
        // Act
        var isRegistered = this.registry.IsRegistered(typeof(TestMessage));

        // Assert
        isRegistered.Should().BeFalse();
    }

    [TestMethod]
    public void GetRegisteredMessageTypes_ReturnsAllRegisteredTypes()
    {
        // Arrange
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();
        this.registry.RegisterHandler<AnotherTestMessage, AnotherTestMessageHandler>();

        // Act
        var messageTypes = this.registry.GetRegisteredMessageTypes();

        // Assert
        messageTypes.Should().Contain(typeof(TestMessage));
        messageTypes.Should().Contain(typeof(AnotherTestMessage));
    }

    [TestMethod]
    public void CreateHandler_WhenRegistered_CreatesHandlerInstance()
    {
        // Arrange
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();

        // Act
        var handler = this.registry.CreateHandler(typeof(TestMessage));

        // Assert
        handler.Should().NotBeNull();
        handler.Should().BeOfType<TestMessageHandler>();
    }

    [TestMethod]
    public void CreateHandler_WhenNotRegistered_ThrowsException()
    {
        // Act & Assert
        var act = () => this.registry.CreateHandler(typeof(TestMessage));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No handler registered for message type: *TestMessage*");
    }

    [TestMethod]
    public void CreateScopedHandler_WithScope_CreatesHandlerInstance()
    {
        // Arrange
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();

        using var scope = this.serviceProvider.CreateScope();

        // Act
        var handler = this.registry.CreateScopedHandler(typeof(TestMessage), scope);

        // Assert
        handler.Should().NotBeNull();
        handler.Should().BeOfType<TestMessageHandler>();
    }

    [TestMethod]
    public void CreateScopedHandler_WithNullScope_ThrowsException()
    {
        // Arrange
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>();

        // Act & Assert
        var act = () => this.registry.CreateScopedHandler(typeof(TestMessage), null);
        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void CreateScopedHandler_WhenNotRegistered_ThrowsException()
    {
        // Arrange
        using var scope = this.serviceProvider.CreateScope();

        // Act & Assert
        var act = () => this.registry.CreateScopedHandler(typeof(TestMessage), scope);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No handler registered for message type: *TestMessage*");
    }

    [TestMethod]
    public void RegisterHandler_MultipleTimesForSameType_ReplacesRegistration()
    {
        // Arrange
        var options1 = new HandlerOptions<TestMessage> { MinParallelism = 1 };
        var options2 = new HandlerOptions<TestMessage> { MinParallelism = 5 };

        // Act
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>(options1);
        this.registry.RegisterHandler<TestMessage, TestMessageHandler>(options2);

        // Assert
        var registration = this.registry.GetRegistration(typeof(TestMessage));
        registration.Options.Should().Be(options2);
    }

    // Test message types
    public class TestMessage
    {
        public string Content { get; set; }
    }

    public class AnotherTestMessage
    {
        public int Value { get; set; }
    }

    // Test handler implementations
    public class TestMessageHandler : IMessageHandler<TestMessage>
    {
        public System.Threading.Tasks.Task HandleAsync(TestMessage message, System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    public class AnotherTestMessageHandler : IMessageHandler<AnotherTestMessage>
    {
        public System.Threading.Tasks.Task HandleAsync(AnotherTestMessage message, System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
