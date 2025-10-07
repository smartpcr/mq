// -----------------------------------------------------------------------
// <copyright file="UnityServiceProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.DependencyInjection
{
    using System;
    using Unity;

    /// <summary>
    /// Adapter that allows Unity container to be used as an IServiceProvider.
    /// </summary>
    public class UnityServiceProvider : IServiceProvider, IDisposable
    {
        private readonly IUnityContainer container;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnityServiceProvider"/> class.
        /// </summary>
        /// <param name="container">The Unity container.</param>
        public UnityServiceProvider(IUnityContainer container)
        {
            this.container = container ?? throw new ArgumentNullException(nameof(container));
        }

        /// <inheritdoc/>
        public object GetService(Type serviceType)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            try
            {
                return this.container.Resolve(serviceType);
            }
            catch (ResolutionFailedException)
            {
                // IServiceProvider contract: return null if service cannot be resolved
                return null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!this.disposed)
            {
                this.container?.Dispose();
                this.disposed = true;
            }
        }
    }
}
