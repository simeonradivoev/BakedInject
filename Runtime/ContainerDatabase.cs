using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using UnityEngine;

namespace BakedInject
{
    /// <summary>
    /// Database that holds all baked factories and injectors.
    /// </summary>
    public static class ContainerDatabase
    {
        public static Dictionary<Type, Injector> Injectors = new Dictionary<Type, Injector>();
        public static Dictionary<Type, Factory> Factories = new Dictionary<Type, Factory>();

        public delegate object Factory([NotNull]Container container);
        public delegate void Injector(object instance, [NotNull]Container container);

        /// <summary>
        /// Register a factory method for a given type.
        /// </summary>
        /// <typeparam name="T">The type the factory is for.</typeparam>
        /// <param name="func">Factory method.</param>
        public static void Register<T>([NotNull] Factory func)
        {
            Factories.Add(typeof(T), new Factory(func));
        }

        /// <summary>
        /// Register a injection method for a given type.
        /// </summary>
        /// <typeparam name="T">The type the injection is for.</typeparam>
        /// <param name="action">The injection method.</param>
        public static void Register<T>([NotNull] Injector action)
        {
            Injectors.Add(typeof(T), new Injector(action));
        }

        /// <summary>
        /// Get an injector for a given type.
        /// </summary>
        /// <param name="type">The type to search for.</param>
        /// <returns>The injector.</returns>
        [CanBeNull]
        public static Injector GetInjector([NotNull]Type type)
        {
            if (!Injectors.TryGetValue(type, out var injectAction))
            {
#if ENABLE_REFLECT_INJECTION
                injectAction = (o, c) => ReflectionInjection(type, o, c);
                Injectors.Add(type, injectAction);
#else
                return null;
#endif
            }

            return injectAction;
        }

        /// <summary>
        /// Get a factory for a given type.
        /// </summary>
        /// <param name="type">The type to search for.</param>
        /// <returns>The factory.</returns>
        /// <exception cref="NotSupportedException">If there is no registered factory for a given type.</exception>
        [NotNull]
        public static Factory GetFactory([NotNull]Type type)
        {
            if (!Factories.TryGetValue(type, out var factoryFunc))
            {
#if ENABLE_REFLECT_INJECTION
                factoryFunc = a => ReflectionFactory(type, a);
                Factories.Add(type, factoryFunc);
#else
                throw new NotSupportedException($"No factory for type: {type}");
#endif
            }

            return factoryFunc;
        }

        private static void ReflectionInjection([NotNull]Type type, object instance, [NotNull]Container container)
        {
            var fields = type.GetFields();
            foreach (var field in fields)
            {
                var injectAttribute = field.GetCustomAttribute<InjectAttribute>();
                if (injectAttribute != null)
                {
                    field.SetValue(instance,container.Resolve(field.FieldType));
                }
            }
        }

        private static object ReflectionFactory([NotNull]Type type, [NotNull]Container container)
        {
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                var arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var instance = container.Resolve(parameters[i].ParameterType);
                    arguments[i] = instance;
                }

                return constructor.Invoke(arguments);
            }

            throw new Exception($"No valid constructors found on {type}");
        }
    }
}