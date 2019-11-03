using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace BakedInject
{
    /// <summary>
    /// Injection container.
    /// Here you can bind and resolve instances.
    /// </summary>
    public class Container
    {
        /// <summary>
        /// All registered bindings per type.
        /// </summary>
        private readonly Dictionary<Type, List<Binding>> bindings = new Dictionary<Type, List<Binding>>();

        /// <summary>
        /// Generic binding with generic type constraints.
        /// </summary>
        /// <typeparam name="T">Type of binding.</typeparam>
        public class Binding<T> : Binding
        {
            public Binding() : base(typeof(T))
            {
            }

            public Binding<T> FromInstance([NotNull] T instance)
            {
                if (instance == null)
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                base.FromInstance(instance);
                return this;
            }

            public Binding<T> FromFactory([NotNull] Func<Container, T> instanceFactory)
            {
                this.instanceFactory = c => instanceFactory.Invoke(c);
                return this;
            }

            public Binding<T> FromFactory([NotNull] Func<T> instanceFactory)
            {
                this.instanceFactory = c => instanceFactory.Invoke();
                return this;
            }

            public new Binding<T> AsSingle()
            {
                this.scope = ScopeTypes.Singleton;
                return this;
            }

            public new Binding<T> AsTransient()
            {
                this.scope = ScopeTypes.Transient;
                return this;
            }

            public Binding Base()
            {
                return this;
            }
        }

        /// <summary>
        /// Base binding class.
        /// </summary>
        public class Binding
        {
            public enum ScopeTypes
            {
                Unset,
                Transient,
                Singleton
            }

            public readonly Type type;
            public Type concreteType;
            public object instance;
            public Func<Container, object> instanceFactory;
            public ScopeTypes scope;
            public bool injected;

            public Binding(Type type)
            {
                this.type = type;
                concreteType = type;
            }

            public Binding To<T>()
            {
                concreteType = typeof(T);
                return this;
            }

            public Binding FromInstance([NotNull] object instance)
            {
                if (!type.IsInstanceOfType(instance))
                {
                    throw new Exception($"Instance is not of type: {type}");
                }

                this.instance = instance;
                this.scope = ScopeTypes.Singleton;
                return this;
            }

            public Binding FromInstance<T>() where T : new()
            {
                this.instance = new T();
                this.scope = ScopeTypes.Singleton;
                return this;
            }

            public Binding FromFactory([NotNull] Func<Container, object> instanceFactory)
            {
                this.instanceFactory = instanceFactory;
                return this;
            }

            public Binding FromFactory([NotNull] Func<object> instanceFactory)
            {
                this.instanceFactory = c => instanceFactory.Invoke();
                return this;
            }

            public Binding AsSingle()
            {
                this.scope = ScopeTypes.Singleton;
                return this;
            }

            public Binding AsTransient()
            {
                this.scope = ScopeTypes.Transient;
                return this;
            }
        }

        public Binding Bind(Type type)
        {
            var binding = new Binding(type);
            if (!bindings.TryGetValue(type, out var existing))
            {
                existing = new List<Binding>();
                bindings.Add(type, existing);
            }

            existing.Add(binding);
            return binding;
        }

        public Binding<T> Bind<T>()
        {
            var binding = new Binding<T>();
            if (!bindings.TryGetValue(typeof(T), out var existing))
            {
                existing = new List<Binding>();
                bindings.Add(typeof(T), existing);
            }

            existing.Add(binding);
            return binding;
        }

        public Binding Bind<T>(T val)
        {
            return Bind<T>().FromInstance(val);
        }

        public Binding BindNew<T>() where T : new()
        {
            return Bind<T>().FromInstance<T>();
        }

        private bool TryFindBindings([NotNull] Type type, out List<Binding> bindingsOut)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            bindingsOut = null;

            while (type != null && !bindings.TryGetValue(type, out bindingsOut))
            {
                type = type.BaseType;
            }

            return bindingsOut != null;
        }

        private object CreateNewInstance(Binding binding)
        {
            object instance;

            if (binding.instance != null)
            {
                instance = binding.instance;
            }
            else if (binding.instanceFactory != null)
            {
                instance = binding.instanceFactory.Invoke(this);
            }
            else
            {
                var factory = ContainerDatabase.GetFactory(binding.concreteType);
                instance = factory.Invoke(this);
            }

            return instance;
        }

        /// <summary>
        /// Try and inject an object.
        /// </summary>
        /// <param name="obj">The object to inject.</param>
        /// <returns>Was there an registered injector for the given object.</returns>
        public bool TryInject(object obj)
        {
            var injector = ContainerDatabase.GetInjector(obj.GetType());
            if (injector != null)
            {
                injector.Invoke(obj, this);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try and resolve and object of type.
        /// </summary>
        /// <param name="type">The type of object to resolve.</param>
        /// <param name="obj">The resolved object.</param>
        /// <returns>Was there a valid binding for the specified object type.</returns>
        public bool TryResolve(Type type, out object obj)
        {
            obj = null;
            try
            {
                obj = Resolve(type);
                return true;
            }
            catch (InvalidCastException)
            {
                // ignored
            }
            catch (KeyNotFoundException)
            {
                // ignored
            }

            return false;
        }

        /// <summary>
        /// Resolve an object of type.
        /// </summary>
        /// <param name="type">Type of object to resolve.</param>
        public object Resolve([NotNull] Type type)
        {
            bool found = TryFindBindings(type, out var foundBindings);
            if (!found || foundBindings.Count <= 0)
            {
                throw new KeyNotFoundException($"Could not find binding for type: {type}");
            }

            object instance;
            bool invalidType = false;
            var firstBinding = foundBindings[0];
            if (firstBinding.injected)
            {
                instance = firstBinding.instance;
                if (!type.IsInstanceOfType(instance))
                {
                    invalidType = true;
                }
            }
            else
            {
                instance = CreateNewInstance(firstBinding);
                if (type.IsInstanceOfType(instance))
                {
                    //inject
                    ContainerDatabase.GetInjector(type)?.Invoke(instance, this);

                    if (firstBinding.scope == Binding.ScopeTypes.Unset)
                    {
                        throw new InvalidOperationException($"Undefined Scope for binding of type: {type}");
                    }

                    //mark injected if singleton
                    if (firstBinding.scope == Binding.ScopeTypes.Singleton)
                    {
                        firstBinding.instance = instance;
                        firstBinding.injected = true;
                    }
                }
                else
                {
                    invalidType = true;
                }
            }

            if (invalidType)
            {
                throw new InvalidCastException(
                    $"Binding for type: {type} has an instance value of type {instance.GetType()}");
            }

            return instance;
        }

        /// <summary>
        /// Resolve an object of type.
        /// </summary>
        /// <typeparam name="T">The type of object to resolve.</typeparam>
        /// <returns>The resolved object.</returns>
        public T Resolve<T>()
        {
            return (T) Resolve(typeof(T));
        }

        /// <summary>
        /// Try and resolve and object of type.
        /// </summary>
        /// <typeparam name="T">The type of object to resolve.</typeparam>
        /// <param name="objT">The resolved object.</param>
        /// <returns>Was there a valid binding for the specified object type.</returns>
        public bool TryResolve<T>(out T objT)
        {
            objT = default;
            if (!TryResolve(typeof(T), out var obj))
                return false;
            objT = (T) obj;
            return true;
        }
    }
}