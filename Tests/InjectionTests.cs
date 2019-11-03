using System;
using NUnit.Framework;

namespace BakedInject.Tests
{
    public class InjectionTests : ContainerTestsBase
    {
        [Test]
        public void PublicFieldInjectPassed()
        {
            container.Bind("Test");
            container.BindNew<PublicFieldInjectionObjectMock>().AsSingle();
            var instance = container.Resolve<PublicFieldInjectionObjectMock>();
            Assert.AreEqual("Test", instance.test);
        }

        [Test]
        public void PrivateFieldInjectPassed()
        {
            container.Bind("Test");
            container.BindNew<PrivateFieldInjectionObjectMock>().AsSingle();
            var instance = container.Resolve<PrivateFieldInjectionObjectMock>();
            Assert.AreEqual("Test", instance.GetTest());
        }

        [Test]
        public void PublicConstructorInternalTypeInjectPassed()
        {
            container.Bind("Test");
            container.Bind<PublicConstructorInternalClassInjectionObjectMock>().AsSingle();
            var instance = container.Resolve<PublicConstructorInternalClassInjectionObjectMock>();
            Assert.AreEqual("Test", instance.test);
        }

        [Test]
        public void InternalConstructorPublicTypeInjectPassed()
        {
            container.Bind("Test");
            container.Bind<PrivateConstructorPublicClassInjectionObjectMock>().AsSingle();
            var instance = container.Resolve<PrivateConstructorPublicClassInjectionObjectMock>();
            Assert.AreEqual("Test", instance.test);
        }

        [Test]
        public void PrivateConstructorPrivateTypeInjectPassed()
        {
            container.Bind("Test");
            container.Bind<PrivateConstructorPrivateClassInjectionObjectMock>().AsSingle();
            var instance = container.Resolve<PrivateConstructorPrivateClassInjectionObjectMock>();
            Assert.AreEqual("Test", instance.test);
        }

        [Test]
        public void FactoryResolvePasses()
        {
            container.Bind("Test");
            container
                .Bind<NonBakedInjectionObjectMock>()
                .AsSingle()
                .FromFactory(c => new NonBakedInjectionObjectMock(c.Resolve<string>()));
            var instance = container.Resolve<NonBakedInjectionObjectMock>();
            Assert.AreEqual("Test", instance.test);
        }

        [Test]
        public void TransientFactoryResolveMultipleCallsPasses()
        {
            int createCount = 0;

            container.Bind<string>().AsTransient().FromFactory(() =>
            {
                createCount++;
                return "Test";
            });

            Assert.AreEqual("Test", container.Resolve<string>());
            Assert.AreEqual("Test", container.Resolve<string>());

            Assert.AreEqual(2,createCount);
        }

        [Test]
        public void SingeltonFactoryResolveSingleCallPasses()
        {
            int createCount = 0;

            container.Bind<string>().AsSingle().FromFactory(() =>
            {
                createCount++;
                return "Test";
            });

            Assert.AreEqual("Test", container.Resolve<string>());
            Assert.AreEqual("Test", container.Resolve<string>());

            Assert.AreEqual(1, createCount);
        }

        [Test]
        public void NoScopeSetFails()
        {
            container.Bind<string>().FromFactory(() => "Test");

            Assert.Catch<InvalidOperationException>(() =>
            {
                container.Resolve<string>();
            });
        }

        private class NonBakedInjectionObjectMock
        {
            public readonly string test;

            public NonBakedInjectionObjectMock(string test)
            {
                this.test = test;
            }
        }

        private class PrivateConstructorPrivateClassInjectionObjectMock
        {
            public readonly string test;

            [Inject]
            private PrivateConstructorPrivateClassInjectionObjectMock(string test)
            {
                this.test = test;
            }

        }

        public class PrivateConstructorPublicClassInjectionObjectMock
        {
            public readonly string test;

            [Inject]
            internal PrivateConstructorPublicClassInjectionObjectMock(string test)
            {
                this.test = test;
            }
        }

        internal class PublicConstructorInternalClassInjectionObjectMock
        {
            public readonly string test;

            [Inject]
            public PublicConstructorInternalClassInjectionObjectMock(string test)
            {
                this.test = test;
            }
        }

        private class PublicFieldInjectionObjectMock
        {
            [Inject]
            public string test;
        }

        private class PrivateFieldInjectionObjectMock
        {
            [Inject]
            private string test;

            public string GetTest() => test;
        }
    }
}