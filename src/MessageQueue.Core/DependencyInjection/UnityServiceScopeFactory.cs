// -----------------------------------------------------------------------
// <copyright file="UnityServiceScopeFactory.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.DependencyInjection
{
    using System;
    using Microsoft.Extensions.DependencyInjection;
    using Unity;

    /// <summary>
    /// Unity implementation of IServiceScopeFactory.
    /// </summary>
    public class UnityServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IUnityContainer container;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnityServiceScopeFactory"/> class.
        /// </summary>
        /// <param name="container">The Unity container.</param>
        public UnityServiceScopeFactory(IUnityContainer container)
        {
            this.container = container ?? throw new ArgumentNullException(nameof(container));
        }

        /// <inheritdoc/>
        public IServiceScope CreateScope()
        {
            return new UnityServiceScope(this.container.CreateChildContainer());
        }

        private class UnityServiceScope : IServiceScope
        {
            private readonly IUnityContainer scopedContainer;
            private bool disposed;

            public UnityServiceScope(IUnityContainer scopedContainer)
            {
                this.scopedContainer = scopedContainer ?? throw new ArgumentNullException(nameof(scopedContainer));
                this.ServiceProvider = new UnityServiceProvider(scopedContainer);
            }

            public IServiceProvider ServiceProvider { get; }

            public void Dispose()
            {
                if (!this.disposed)
                {
                    this.scopedContainer?.Dispose();
                    this.disposed = true;
                }
            }
        }
    }
}
