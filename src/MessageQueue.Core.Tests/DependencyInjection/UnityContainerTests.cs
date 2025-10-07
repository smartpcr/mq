// -----------------------------------------------------------------------
// <copyright file="UnityContainerTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests.DependencyInjection
{
    using System;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Unity;
    using MessageQueue.Core.DependencyInjection;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Options;

    [TestClass]
    public class UnityContainerTests
    {
        [TestMethod]
        public void AddMessageQueue_RegistersAllCoreServices()
        {
            // Arrange
            var container = new UnityContainer();

            // Act
            container.AddMessageQueue(options =>
            {
                options.Capacity = 100;
                options.EnablePersistence = false;
            });

            // Assert
            container.Resolve<QueueOptions>().Should().NotBeNull();
            container.Resolve<IQueueManager>().Should().NotBeNull();
            container.Resolve<IQueuePublisher>().Should().NotBeNull();
            container.Resolve<IDeadLetterQueue>().Should().NotBeNull();
            container.Resolve<HandlerRegistry>().Should().NotBeNull();
            container.Resolve<IHandlerDispatcher>().Should().NotBeNull();
            container.Resolve<IQueueAdminApi>().Should().NotBeNull();
            container.Resolve<ILeaseMonitor>().Should().NotBeNull();
            container.Resolve<IHeartbeatService>().Should().NotBeNull();
        }

        [TestMethod]
        public void AddMessageQueue_ConfiguresOptions()
        {
            // Arrange
            var container = new UnityContainer();

            // Act
            container.AddMessageQueue(options =>
            {
                options.Capacity = 500;
                options.EnablePersistence = false;
                options.DefaultMaxRetries = 10;
            });

            var resolvedOptions = container.Resolve<QueueOptions>();

            // Assert
            resolvedOptions.Capacity.Should().Be(500);
            resolvedOptions.EnablePersistence.Should().BeFalse();
            resolvedOptions.DefaultMaxRetries.Should().Be(10);
        }

        [TestMethod]
        public void RegisterMessageHandler_RegistersHandlerSuccessfully()
        {
            // Arrange
            var container = new UnityContainer();
            container.AddMessageQueue(options => options.EnablePersistence = false);

            // Act
            container.RegisterMessageHandler<TestMessage, TestMessageHandler>(options =>
            {
                options.MaxParallelism = 5;
                options.Timeout = TimeSpan.FromSeconds(30);
            });

            var registry = container.Resolve<HandlerRegistry>();

            // Assert
            registry.IsRegistered(typeof(TestMessage)).Should().BeTrue();
            var registration = registry.GetRegistration(typeof(TestMessage));
            registration.Should().NotBeNull();
            registration.HandlerType.Should().Be(typeof(TestMessageHandler));
        }

        [TestMethod]
        public void RegisterMessageHandler_WithFactory_RegistersHandlerSuccessfully()
        {
            // Arrange
            var container = new UnityContainer();
            container.AddMessageQueue(options => options.EnablePersistence = false);

            // Act
            container.RegisterMessageHandler<TestMessage>(
                sp => new TestMessageHandler(),
                options =>
                {
                    options.MaxParallelism = 3;
                    options.Timeout = TimeSpan.FromMinutes(1);
                });

            var registry = container.Resolve<HandlerRegistry>();

            // Assert
            registry.IsRegistered(typeof(TestMessage)).Should().BeTrue();
        }

        [TestMethod]
        public void UnityServiceProvider_ResolvesServices()
        {
            // Arrange
            var container = new UnityContainer();
            container.RegisterSingleton<ITestService, TestService>();

            // Act
            var serviceProvider = new UnityServiceProvider(container);
            var service = serviceProvider.GetService(typeof(ITestService));

            // Assert
            service.Should().NotBeNull();
            service.Should().BeOfType<TestService>();
        }

        [TestMethod]
        public void UnityServiceProvider_ReturnsNullForUnregisteredService()
        {
            // Arrange
            var container = new UnityContainer();

            // Act
            var serviceProvider = new UnityServiceProvider(container);
            var service = serviceProvider.GetService(typeof(ITestService));

            // Assert
            service.Should().BeNull();
        }

        [TestMethod]
        public void UnityServiceScopeFactory_CreatesScopes()
        {
            // Arrange
            var container = new UnityContainer();
            container.RegisterType<ITestService, TestService>();

            // Act
            var scopeFactory = new UnityServiceScopeFactory(container);
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetService(typeof(ITestService));

            // Assert
            service.Should().NotBeNull();
            service.Should().BeOfType<TestService>();
        }

        [TestMethod]
        public void UnityServiceScopeFactory_CreatesSeparateScopes()
        {
            // Arrange
            var container = new UnityContainer();
            container.RegisterType<ITestService, TestService>();

            // Act
            var scopeFactory = new UnityServiceScopeFactory(container);
            object service1;
            object service2;

            using (var scope1 = scopeFactory.CreateScope())
            {
                service1 = scope1.ServiceProvider.GetService(typeof(ITestService));
            }

            using (var scope2 = scopeFactory.CreateScope())
            {
                service2 = scope2.ServiceProvider.GetService(typeof(ITestService));
            }

            // Assert
            service1.Should().NotBeNull();
            service2.Should().NotBeNull();
            service1.Should().NotBeSameAs(service2);
        }

        [TestMethod]
        public void BuildServiceProvider_ReturnsWorkingProvider()
        {
            // Arrange
            var container = new UnityContainer();
            container.AddMessageQueue(options => options.EnablePersistence = false);

            // Act
            var serviceProvider = container.BuildServiceProvider();
            var queueManager = serviceProvider.GetService(typeof(IQueueManager));

            // Assert
            queueManager.Should().NotBeNull();
            queueManager.Should().BeOfType<QueueManager>();
        }

        [TestMethod]
        public async Task EndToEnd_UnityContainer_EnqueueAndProcess()
        {
            // Arrange
            var container = new UnityContainer();
            container.AddMessageQueue(options =>
            {
                options.Capacity = 100;
                options.EnablePersistence = false;
            });

            container.RegisterMessageHandler<TestMessage, TestMessageHandler>(options =>
            {
                options.MaxParallelism = 1;
                options.Timeout = TimeSpan.FromSeconds(5);
            });

            var publisher = container.Resolve<IQueuePublisher>();
            var dispatcher = container.Resolve<IHandlerDispatcher>();

            // Act
            await dispatcher.StartAsync();

            var message = new TestMessage { Content = "Hello Unity" };
            await publisher.EnqueueAsync(message);

            await Task.Delay(1000); // Give time for processing

            await dispatcher.StopAsync();

            // Assert - message should be processed (verify via handler state if needed)
            TestMessageHandler.ProcessedMessages.Should().Contain(m => m.Content == "Hello Unity");
        }

        // Test classes
        public class TestMessage
        {
            public string Content { get; set; }
        }

        public class TestMessageHandler : IMessageHandler<TestMessage>
        {
            public static readonly System.Collections.Concurrent.ConcurrentBag<TestMessage> ProcessedMessages = new();

            public Task HandleAsync(TestMessage message, System.Threading.CancellationToken cancellationToken)
            {
                ProcessedMessages.Add(message);
                return Task.CompletedTask;
            }
        }

        public interface ITestService
        {
        }

        public class TestService : ITestService
        {
        }
    }
}
