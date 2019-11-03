using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace BakedInject.Editor
{
    /// <summary>
    /// Assembly weaver that bakes factories and injectors.
    /// </summary>
    [InitializeOnLoad]
    public static class ReflectionBakerCecil
    {
        static ReflectionBakerCecil()
        {
            //called after assembly compilation and before assembly load even when building.
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        private static void OnAssemblyCompiled(string targetAssembly, CompilerMessage[] messages)
        {
            //don't weave on compile error
            if (messages.Any(m => m.type == CompilerMessageType.Error))
            {
                return;
            }

            Weave(targetAssembly);
        }

        private static void Weave(string targetAssembly)
        {
            const string dependencyInjectionName = "BakedInject";

            // Should not run on own assembly
            if (targetAssembly.EndsWith(dependencyInjectionName + ".dll"))
            {
                return;
            }

            var targetAss = CompilationPipeline.GetAssemblies()
                .FirstOrDefault(a => a.name == Path.GetFileNameWithoutExtension(targetAssembly));

            //check if it references injection assembly
            bool usesInjection = targetAss != null && targetAss.allReferences.Any(d => d.Contains(dependencyInjectionName));

            if (usesInjection)
            {
                TryWeaveAssembly(targetAssembly);
            }
        }

        private static bool NeedsConstructor(TypeDefinition typeDef, TypeReference injectAttribute)
        {
            return typeDef.Methods.Any(m =>
                m.IsConstructor && m.HasCustomAttributes &&
                m.CustomAttributes.Any(a => a.AttributeType.FullName == injectAttribute.FullName));
        }

        private static bool NeedsFields(TypeDefinition typeDef, TypeReference injectAttribute)
        {
            return typeDef.Fields.Any(m => m.HasCustomAttributes &&
                       m.CustomAttributes.Any(a => a.AttributeType.FullName == injectAttribute.FullName));
        }

        private static MethodDefinition GetConstructor(TypeDefinition typeDef, TypeReference injectAttribute)
        {
            return typeDef.Methods.FirstOrDefault(m =>
                m.IsConstructor && m.HasCustomAttributes &&
                m.CustomAttributes.Any(a => a.AttributeType.FullName == injectAttribute.FullName));
        }

        private static void TryWeaveAssembly(string assPath)
        {
            //check if assembly is editor only for applying the InitializeOnLoadMethodAttribute method instead of RuntimeInitializeOnLoadMethodAttribute.
            //used mainly assemblies that run tests.
            bool isEditor = CompilationPipeline.GetAssemblies().Any(a => a.flags == AssemblyFlags.EditorAssembly && assPath.EndsWith(a.outputPath));

            using (var assembly = AssemblyDefinition.ReadAssembly(assPath, new ReaderParameters()
            {
                ReadWrite = true,
                ReadingMode = ReadingMode.Immediate,
                AssemblyResolver = new DefaultAssemblyResolver(),
                SymbolReaderProvider = new PdbReaderProvider(),
                //need to keep it in memory so we can write it back to the file. Otherwise it throws a sharing violation.
                InMemory = true,
                ReadSymbols = true
            }))
            {
                var sourceModule = assembly.MainModule;
                var injectionModule = assembly.MainModule;

                var attributeType = isEditor ? typeof(InitializeOnLoadMethodAttribute) : typeof(RuntimeInitializeOnLoadMethodAttribute);
                var construction = injectionModule.ImportReference(attributeType.GetConstructor(Type.EmptyTypes));
                var customAttribute = new CustomAttribute(injectionModule.ImportReference(construction));
                var containerParameter = new ParameterDefinition("container", ParameterAttributes.None,
                    injectionModule.ImportReference(typeof(Container)));

                var constructorTypes = new List<TypeDefinition>();
                var injectionTypes = new List<TypeDefinition>();

                foreach (var type in sourceModule.GetTypes())
                {
                    var injectRef = sourceModule.ImportReference(typeof(InjectAttribute));
                    if (NeedsConstructor(type, injectRef))
                    {
                        constructorTypes.Add(type);
                        Debug.Log($"Baking Factory for type: {type.FullName}");
                    }

                    if (NeedsFields(type, injectRef))
                    {
                        injectionTypes.Add(type);
                        Debug.Log($"Baking Injector for type: {type.FullName}");
                    }
                }

                if (constructorTypes.Count > 0)
                {
                    var injectAttributeRef = injectionModule.ImportReference(typeof(InjectAttribute));
                    var factoryMethods = new List<KeyValuePair<TypeReference, MethodDefinition>>();
                    var injectionMethods = new List<KeyValuePair<TypeReference, MethodDefinition>>();

                    //type where all static injection baking processing is done.
                    var typeAdder = new TypeDefinition("Injection", "Baker",
                        TypeAttributes.Class | TypeAttributes.Public, injectionModule.TypeSystem.Object);
                    injectionModule.Types.Add(typeAdder);

                    //method where all registration is done.
                    var bakerMethod = new MethodDefinition("BakeInjection",
                        MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig,
                        injectionModule.TypeSystem.Void);
                    typeAdder.Methods.Add(bakerMethod);

                    //create factories for constructor marked with the inject attribute.
                    foreach (var type in constructorTypes)
                    {
                        ExposeType(type);

                        var typeRef = injectionModule.ImportReference(type);
                        var factoryMethod = new MethodDefinition(type.Name + "FactoryMethod",
                            MethodAttributes.Static | MethodAttributes.Assembly,
                            injectionModule.TypeSystem.Object);

                        factoryMethod.Parameters.Add(containerParameter);
                        type.Methods.Add(factoryMethod);
                        CreateFactoryMethodBody(factoryMethod, type, injectionModule, injectAttributeRef);
                        factoryMethods.Add(new KeyValuePair<TypeReference, MethodDefinition>(typeRef, factoryMethod));
                    }

                    //create injection static methods that resolve each field marked with inject attribute.
                    foreach (var type in injectionTypes)
                    {
                        ExposeType(type);

                        var typeRef = injectionModule.ImportReference(type);
                        var injectionMethod = new MethodDefinition(type.Name + "InjectionMethod",
                            MethodAttributes.Static | MethodAttributes.Assembly,
                            injectionModule.TypeSystem.Void);
                        injectionMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, injectionModule.TypeSystem.Object));
                        injectionMethod.Parameters.Add(containerParameter);
                        type.Methods.Add(injectionMethod);
                        CreateInjectionMethodBody(injectionMethod, type, injectionModule, injectAttributeRef);
                        injectionMethods.Add(new KeyValuePair<TypeReference, MethodDefinition>(typeRef, injectionMethod));
                    }

                    CreateBakeMethodBody(bakerMethod, factoryMethods,injectionMethods, injectionModule);
                    bakerMethod.CustomAttributes.Add(customAttribute);

                    var writeParams = new WriterParameters()
                    {
                        //write symbols for debugging to work
                        WriteSymbols = true,
                        SymbolWriterProvider = new PdbWriterProvider()
                    };

                    assembly.Write(assPath,writeParams);
                }
            }
        }

        /// <summary>
        /// Create final registration method.
        /// </summary>
        private static void CreateBakeMethodBody(MethodDefinition method,
            List<KeyValuePair<TypeReference, MethodDefinition>> factoryMethods, 
            List<KeyValuePair<TypeReference, MethodDefinition>> injectionMethods, 
            ModuleDefinition module)
        {
            var processor = method.Body.GetILProcessor();

            var factoryDelegateConstructor =
                module.ImportReference(typeof(ContainerDatabase.Factory).GetConstructors().First());

            var injectorDelegateConstructor =
                module.ImportReference(typeof(ContainerDatabase.Injector).GetConstructors().First());

            var genericRegisterFactoryMethod =
                module.ImportReference(
                    typeof(ContainerDatabase).GetMethod("Register", new[] {typeof(ContainerDatabase.Factory)}));

            var genericRegisterInjectorMethod =
                module.ImportReference(
                    typeof(ContainerDatabase).GetMethod("Register", new[] { typeof(ContainerDatabase.Injector) }));

            foreach (var factoryMethod in factoryMethods)
            {
                var genericRegisterMethodInstance = new GenericInstanceMethod(genericRegisterFactoryMethod);
                genericRegisterMethodInstance.GenericArguments.Add(factoryMethod.Key);

                processor.Emit(OpCodes.Ldnull);
                processor.Emit(OpCodes.Ldftn, factoryMethod.Value);
                processor.Emit(OpCodes.Newobj, factoryDelegateConstructor);
                processor.Emit(OpCodes.Call, genericRegisterMethodInstance);
            }

            foreach (var injectionMethod in injectionMethods)
            {
                var genericRegisterMethodInstance = new GenericInstanceMethod(genericRegisterInjectorMethod);
                genericRegisterMethodInstance.GenericArguments.Add(injectionMethod.Key);

                processor.Emit(OpCodes.Ldnull);
                processor.Emit(OpCodes.Ldftn, injectionMethod.Value);
                processor.Emit(OpCodes.Newobj, injectorDelegateConstructor);
                processor.Emit(OpCodes.Call, genericRegisterMethodInstance);
            }

            processor.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Create constructor factory method.
        /// </summary>
        private static void CreateFactoryMethodBody(MethodDefinition method, TypeDefinition typeDef,
            ModuleDefinition module, TypeReference customAttribute)
        {
            var genericGetter = module.ImportReference(typeof(Container).GetMethod("Resolve", Type.EmptyTypes));
            var constructorDef = GetConstructor(typeDef, customAttribute);
            var typeConstructor = module.ImportReference(constructorDef);
            var processor = method.Body.GetILProcessor();

            foreach (var parameter in typeConstructor.Parameters)
            {
                var genericGetterInstance = new GenericInstanceMethod(genericGetter);
                genericGetterInstance.GenericArguments.Add(parameter.ParameterType);

                processor.Emit(OpCodes.Ldarg_0);    //container parameter
                processor.Emit(OpCodes.Callvirt, genericGetterInstance);
            }

            processor.Emit(OpCodes.Newobj, typeConstructor);
            processor.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Create injection method.
        /// </summary>
        private static void CreateInjectionMethodBody(MethodDefinition method, TypeDefinition typeDef, ModuleDefinition module, TypeReference customAttribute)
        {
            var genericGetter = module.ImportReference(typeof(Container).GetMethod("Resolve", Type.EmptyTypes));
            var processor = method.Body.GetILProcessor();

            foreach (var field in typeDef.Fields)
            {
                if (field.HasCustomAttributes && field.CustomAttributes.Any(a => a.AttributeType.FullName == customAttribute.FullName))
                {
                    var genericGetterInstance = new GenericInstanceMethod(genericGetter);
                    genericGetterInstance.GenericArguments.Add(field.FieldType);
                    //todo probably safer to cast object to source type
                    processor.Emit(OpCodes.Ldarg_0);    //instance parameter
                    processor.Emit(OpCodes.Ldarg_1);    //container parameter
                    processor.Emit(OpCodes.Callvirt, genericGetterInstance);
                    processor.Emit(OpCodes.Stfld,module.ImportReference(field));
                }
            }

            processor.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Expose a type and make it public.
        /// </summary>
        /// <param name="definition"></param>
        private static void ExposeType(TypeDefinition definition)
        {
            //todo should make private fields internal and keep internal ones.
            definition.Attributes = TypeAttributes.Public;
        }
    }
}