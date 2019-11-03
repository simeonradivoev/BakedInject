using System;
using NUnit.Framework;

namespace BakedInject.Tests
{
    public class NonBakedReflectionTests : ContainerTestsBase
    {
        // A Test behaves as an ordinary method
        [Test]
        public void PublicNonBakedConstructorInjectionFails()
        {
            container.Bind<string>().FromInstance("Test");
            container.Bind<NoInjectionAttributePublicConstructorMock>().AsSingle();
            Assert.Catch<NotSupportedException>(() =>
            {
                var instance = container.Resolve<NoInjectionAttributePublicConstructorMock>();
            });
        }

        [Test]
        public void PrivateNonBakedConstructorInjectionFails()
        {
            container.Bind<string>().FromInstance("Test");
            container.Bind<NoInjectionAttributePrivateConstructorMock>().AsSingle();
            Assert.Catch<NotSupportedException>(() =>
            {
                var instance = container.Resolve<NoInjectionAttributePrivateConstructorMock>();
            });
        }

        public class NoInjectionAttributePublicConstructorMock
        {
            public string test;

            public NoInjectionAttributePublicConstructorMock(string test)
            {
                this.test = test;
            }
        }

        public class NoInjectionAttributePrivateConstructorMock
        {
            public string test;

            private NoInjectionAttributePrivateConstructorMock(string test)
            {
                this.test = test;
            }
        }
    }
}
