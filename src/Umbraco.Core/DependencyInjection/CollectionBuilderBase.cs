using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LightInject;

namespace Umbraco.Core.DependencyInjection
{
    /// <summary>
    /// Provides a base class for collection builders.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the builder.</typeparam>
    /// <typeparam name="TCollection">The type of the collection.</typeparam>
    /// <typeparam name="TItem">The type of the items.</typeparam>
    public abstract class CollectionBuilderBase<TBuilder, TCollection, TItem> : ICollectionBuilder<TCollection, TItem>
        where TBuilder: CollectionBuilderBase<TBuilder, TCollection, TItem>
        where TCollection : IBuilderCollection<TItem>
    {
        private readonly List<Type> _types = new List<Type>();
        private readonly object _locker = new object();
        private Func<IEnumerable<TItem>, TCollection> _collectionCtor;
        private ServiceRegistration[] _registrations;

        /// <summary>
        /// Initializes a new instance of the <see cref="CollectionBuilderBase{TBuilder, TCollection,TItem}"/>
        /// class with a service container.
        /// </summary>
        /// <param name="container">A service container.</param>
        protected CollectionBuilderBase(IServiceContainer container)
        {
            Container = container;
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            Initialize();
        }

        /// <summary>
        /// Gets the service container.
        /// </summary>
        protected IServiceContainer Container { get; }

        /// <summary>
        /// Initializes a new instance of the builder.
        /// </summary>
        /// <remarks>This is called by the constructor and, by default, registers the
        /// collection automatically.</remarks>
        protected virtual void Initialize()
        {
            // compile the auto-collection constructor
            var argType = typeof(IEnumerable<TItem>);
            var ctorArgTypes = new[] { argType };
            var constructor = typeof(TCollection).GetConstructor(ctorArgTypes);
            if (constructor == null) throw new InvalidOperationException();
            var exprArg = Expression.Parameter(argType, "items");
            var exprNew = Expression.New(constructor, exprArg);
            var expr = Expression.Lambda<Func<IEnumerable<TItem>, TCollection>>(exprNew, exprArg);
            _collectionCtor = expr.Compile();

            // register the collection
            Container.Register(_ => CreateCollection(), CollectionLifetime);
        }

        /// <summary>
        /// Gets the collection lifetime.
        /// </summary>
        /// <remarks>Return null for transient collections.</remarks>
        protected virtual ILifetime CollectionLifetime => new PerContainerLifetime();

        /// <summary>
        /// Registers the collection builder into a service container.
        /// </summary>
        /// <param name="container">The service container.</param>
        /// <remarks>The collection builder is registered with a "per container" lifetime,
        /// and the collection is registered wiht a lifetime that is "per container" by
        /// default but can be overriden by each builder implementation.</remarks>
        public static TBuilder Register(IServiceContainer container)
        {
            // register the builder - per container
            var builderLifetime = new PerContainerLifetime();
            container.Register<TBuilder>(builderLifetime);

            // return the builder
            // also initializes the builder
            return container.GetInstance<TBuilder>();
        }

        /// <summary>
        /// Gets the collection builder from a service container.
        /// </summary>
        /// <param name="container">The service container.</param>
        /// <returns>The collection builder.</returns>
        public static TBuilder Get(IServiceContainer container)
        {
            return container.GetInstance<TBuilder>();
        }

        /// <summary>
        /// Configures the internal list of types.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <remarks>Throws if the types have already been registered.</remarks>
        protected void Configure(Action<List<Type>> action)
        {
            lock (_locker)
            {
                if (_registrations != null)
                    throw new InvalidOperationException("Cannot configure a collection builder after its types have been resolved.");
                action(_types);
            }
        }

        /// <summary>
        /// Gets the types.
        /// </summary>
        /// <param name="types">The internal list of types.</param>
        /// <returns>The list of types to register.</returns>
        protected virtual IEnumerable<Type> GetTypes(IEnumerable<Type> types)
        {
            return types;
        }

        private void RegisterTypes()
        {
            lock (_locker)
            {
                if (_registrations != null) return;

                var prefix = GetType().FullName + "_";
                var i = 0;
                foreach (var type in GetTypes(_types))
                {
                    var name = $"{prefix}{i++:00000}";
                    Container.Register(typeof(TItem), type, name);
                }

                _registrations = Container.AvailableServices
                    .Where(x => x.ServiceName.StartsWith(prefix))
                    .OrderBy(x => x.ServiceName)
                    .ToArray();
            }
        }

        /// <summary>
        /// Creates the collection items.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>The collection items.</returns>
        protected virtual IEnumerable<TItem> CreateItems(params object[] args)
        {
            RegisterTypes(); // will do it only once

            var type = typeof (TItem);
            return _registrations
                .Select(x => (TItem) Container.GetInstance(type, x.ServiceName, args))
                .ToArray(); // safe
        }

        /// <summary>
        /// Creates a collection.
        /// </summary>
        /// <returns>A collection.</returns>
        /// <remarks>Creates a new collection each time it is invoked.</remarks>
        public virtual TCollection CreateCollection()
        {
            if (_collectionCtor == null) throw new InvalidOperationException("Collection auto-creation is not possible.");
            return _collectionCtor(CreateItems());
        }
    }
}