// -----------------------------------------------------------------------
// <copyright file="UnityContainerExtensions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.DependencyInjection
{
    using System;
    using Microsoft.Extensions.DependencyInjection;
    using Unity;
    using Unity.Lifetime;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Options;
    using MessageQueue.Core.Trace;

    /// <summary>
    /// Extension methods for registering MessageQueue services with Unity container.
    /// </summary>
    public static class UnityContainerExtensions
    {
        /// <summary>
        /// Adds all core MessageQueue services to the Unity container.
        /// </summary>
        /// <param name="container">The Unity container.</param>
        /// <param name="configureOptions">Optional action to configure queue options.</param>
        /// <returns>The Unity container for chaining.</returns>
        public static IUnityContainer AddMessageQueue(
            this IUnityContainer container,
            Action<QueueOptions> configureOptions = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            // Configure options
            var options = new QueueOptions();
            configureOptions?.Invoke(options);
            container.RegisterInstance(options);

            // Register IServiceProvider adapter
            container.RegisterFactory<IServiceProvider>(
                c => new UnityServiceProvider(c),
                new ContainerControlledLifetimeManager());

            // Register IServiceScopeFactory
            container.RegisterFactory<IServiceScopeFactory>(
                c => new UnityServiceScopeFactory(c),
                new ContainerControlledLifetimeManager());

            // Register core services
            container.RegisterFactory<ICircularBuffer>(
                c => new CircularBuffer(c.Resolve<QueueOptions>().Capacity),
                new ContainerControlledLifetimeManager());

            container.RegisterSingleton<DeduplicationIndex>();

            // Register QueueManager with null dependencies to break circular dependency
            // Dependencies will be set via setters after registration
            container.RegisterFactory<IQueueManager>(
                c => new QueueManager(
                    c.Resolve<ICircularBuffer>(),
                    c.Resolve<DeduplicationIndex>(),
                    c.Resolve<QueueOptions>(),
                    options.EnablePersistence ? c.Resolve<IPersister>() : null,
                    null, // deadLetterQueue - set via BuildServiceProvider
                    null), // dispatcher - set via BuildServiceProvider
                new ContainerControlledLifetimeManager());

            container.RegisterSingleton<IQueuePublisher, QueuePublisher>();
            container.RegisterSingleton<IDeadLetterQueue, DeadLetterQueue>();
            container.RegisterSingleton<HandlerRegistry>();
            container.RegisterSingleton<IHandlerDispatcher, HandlerDispatcher>();
            container.RegisterSingleton<IQueueAdminApi, QueueAdminApi>();

            // Register persistence services
            if (options.EnablePersistence)
            {
                container.RegisterSingleton<IPersister, Persistence.FilePersister>();
            }

            // Register monitoring services
            container.RegisterSingleton<ILeaseMonitor, LeaseMonitor>();
            container.RegisterSingleton<IHeartbeatService, HeartbeatService>();

            // Register telemetry
            container.RegisterFactory<MessageQueueTelemetry>(
                c => new MessageQueueTelemetry(
                    options.TelemetryProvider,
                    options.EnableOtlpExport,
                    options.OtlpEndpoint),
                new ContainerControlledLifetimeManager());

            // Wire up circular dependencies after all services are registered
            // This resolves the services and sets up the bidirectional references
            container.WireUpMessageQueueDependencies();

            return container;
        }

        /// <summary>
        /// Wires up circular dependencies between QueueManager, DeadLetterQueue, and HandlerDispatcher.
        /// This is called automatically by AddMessageQueue.
        /// </summary>
        /// <param name="container">The Unity container.</param>
        /// <returns>The Unity container for chaining.</returns>
        internal static IUnityContainer WireUpMessageQueueDependencies(this IUnityContainer container)
        {
            // Resolve services to trigger singleton creation
            var queueManager = container.Resolve<IQueueManager>() as QueueManager;
            if (queueManager != null)
            {
                var deadLetterQueue = container.Resolve<IDeadLetterQueue>();
                var dispatcher = container.Resolve<IHandlerDispatcher>();
                queueManager.SetDeadLetterQueue(deadLetterQueue);
                queueManager.SetDispatcher(dispatcher);
            }

            return container;
        }

        /// <summary>
        /// Registers a message handler in the Unity container.
        /// </summary>
        /// <typeparam name="TMessage">The message type.</typeparam>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <param name="container">The Unity container.</param>
        /// <param name="configureOptions">Optional action to configure handler options.</param>
        /// <returns>The Unity container for chaining.</returns>
        public static IUnityContainer RegisterMessageHandler<TMessage, THandler>(
            this IUnityContainer container,
            Action<HandlerOptions<TMessage>> configureOptions = null)
            where THandler : IMessageHandler<TMessage>
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            // Register handler with per-resolve lifetime (new instance per resolution)
            container.RegisterType<THandler>(new PerResolveLifetimeManager());

            // Configure options
            var options = new HandlerOptions<TMessage>();
            configureOptions?.Invoke(options);

            // Register with HandlerRegistry
            var registry = container.Resolve<HandlerRegistry>();
            registry.RegisterHandler<TMessage, THandler>(options);

            return container;
        }

        /// <summary>
        /// Registers a message handler with a custom factory function.
        /// </summary>
        /// <typeparam name="TMessage">The message type.</typeparam>
        /// <param name="container">The Unity container.</param>
        /// <param name="handlerFactory">Factory function to create handler instances.</param>
        /// <param name="configureOptions">Optional action to configure handler options.</param>
        /// <returns>The Unity container for chaining.</returns>
        public static IUnityContainer RegisterMessageHandler<TMessage>(
            this IUnityContainer container,
            Func<IServiceProvider, IMessageHandler<TMessage>> handlerFactory,
            Action<HandlerOptions<TMessage>> configureOptions = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (handlerFactory == null)
            {
                throw new ArgumentNullException(nameof(handlerFactory));
            }

            // Configure options
            var options = new HandlerOptions<TMessage>();
            configureOptions?.Invoke(options);

            // Register with HandlerRegistry
            var registry = container.Resolve<HandlerRegistry>();
            registry.RegisterHandler<TMessage>(handlerFactory, options);

            return container;
        }

        /// <summary>
        /// Builds and returns an IServiceProvider from the Unity container.
        /// Note: Circular dependencies are already wired up by AddMessageQueue.
        /// </summary>
        /// <param name="container">The Unity container.</param>
        /// <returns>An IServiceProvider wrapping the Unity container.</returns>
        public static IServiceProvider BuildServiceProvider(this IUnityContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            return new UnityServiceProvider(container);
        }
    }
}
