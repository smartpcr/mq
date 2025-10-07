// -----------------------------------------------------------------------
// <copyright file="HandlerRegistry.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Microsoft.Extensions.DependencyInjection;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Options;

    /// <summary>
    /// Registry for message handlers with type-based lookup and DI integration.
    /// </summary>
    public class HandlerRegistry
    {
        private readonly ConcurrentDictionary<Type, HandlerRegistration> registrations;
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// Initializes a new instance of the HandlerRegistry.
        /// </summary>
        /// <param name="serviceProvider">Service provider for DI resolution.</param>
        public HandlerRegistry(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            this.registrations = new ConcurrentDictionary<Type, HandlerRegistration>();
        }

        /// <summary>
        /// Registers a handler for a specific message type.
        /// </summary>
        /// <typeparam name="TMessage">The message type.</typeparam>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <param name="options">Handler configuration options.</param>
        public void RegisterHandler<TMessage, THandler>(HandlerOptions<TMessage> options = null)
            where THandler : IMessageHandler<TMessage>
        {
            var messageType = typeof(TMessage);
            var handlerType = typeof(THandler);

            var registration = new HandlerRegistration
            {
                MessageType = messageType,
                HandlerType = handlerType,
                Options = options ?? new HandlerOptions<TMessage>(),
                HandlerFactory = sp => sp.GetRequiredService<THandler>()
            };

            this.registrations.AddOrUpdate(messageType, registration, (k, v) => registration);
        }

        /// <summary>
        /// Registers a handler with a custom factory function.
        /// </summary>
        /// <typeparam name="TMessage">The message type.</typeparam>
        /// <param name="handlerFactory">Factory function to create handler instances.</param>
        /// <param name="options">Handler configuration options.</param>
        public void RegisterHandler<TMessage>(
            Func<IServiceProvider, IMessageHandler<TMessage>> handlerFactory,
            HandlerOptions<TMessage> options = null)
        {
            if (handlerFactory == null)
                throw new ArgumentNullException(nameof(handlerFactory));

            var messageType = typeof(TMessage);

            var registration = new HandlerRegistration
            {
                MessageType = messageType,
                HandlerType = typeof(IMessageHandler<TMessage>),
                Options = options ?? new HandlerOptions<TMessage>(),
                HandlerFactory = sp => handlerFactory(sp)
            };

            this.registrations.AddOrUpdate(messageType, registration, (k, v) => registration);
        }

        /// <summary>
        /// Gets the handler registration for a specific message type.
        /// </summary>
        /// <param name="messageType">The message type.</param>
        /// <returns>Handler registration, or null if not found.</returns>
        public HandlerRegistration GetRegistration(Type messageType)
        {
            this.registrations.TryGetValue(messageType, out var registration);
            return registration;
        }

        /// <summary>
        /// Checks if a handler is registered for the given message type.
        /// </summary>
        /// <param name="messageType">The message type.</param>
        /// <returns>True if a handler is registered.</returns>
        public bool IsRegistered(Type messageType)
        {
            return this.registrations.ContainsKey(messageType);
        }

        /// <summary>
        /// Gets all registered message types.
        /// </summary>
        /// <returns>Collection of registered message types.</returns>
        public IEnumerable<Type> GetRegisteredMessageTypes()
        {
            return this.registrations.Keys;
        }

        /// <summary>
        /// Creates a handler instance using the service provider.
        /// </summary>
        /// <param name="messageType">The message type.</param>
        /// <returns>Handler instance.</returns>
        public object CreateHandler(Type messageType)
        {
            if (!this.registrations.TryGetValue(messageType, out var registration))
            {
                throw new InvalidOperationException($"No handler registered for message type: {messageType.FullName}");
            }

            return registration.HandlerFactory(this.serviceProvider);
        }

        /// <summary>
        /// Creates a scoped handler instance.
        /// </summary>
        /// <param name="messageType">The message type.</param>
        /// <param name="scope">The service scope.</param>
        /// <returns>Handler instance.</returns>
        public object CreateScopedHandler(Type messageType, IServiceScope scope)
        {
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            if (!this.registrations.TryGetValue(messageType, out var registration))
            {
                throw new InvalidOperationException($"No handler registered for message type: {messageType.FullName}");
            }

            return registration.HandlerFactory(scope.ServiceProvider);
        }
    }

    /// <summary>
    /// Handler registration information.
    /// </summary>
    public class HandlerRegistration
    {
        /// <summary>
        /// The message type this handler processes.
        /// </summary>
        public Type MessageType { get; set; }

        /// <summary>
        /// The handler implementation type.
        /// </summary>
        public Type HandlerType { get; set; }

        /// <summary>
        /// Handler configuration options (stored as object to support generic HandlerOptions&lt;T&gt;).
        /// </summary>
        public object Options { get; set; }

        /// <summary>
        /// Factory function to create handler instances.
        /// </summary>
        public Func<IServiceProvider, object> HandlerFactory { get; set; }

        /// <summary>
        /// Gets the typed handler options.
        /// </summary>
        /// <typeparam name="TMessage">The message type.</typeparam>
        /// <returns>Typed handler options.</returns>
        public HandlerOptions<TMessage> GetOptions<TMessage>()
        {
            return this.Options as HandlerOptions<TMessage> ?? new HandlerOptions<TMessage>();
        }
    }
}
