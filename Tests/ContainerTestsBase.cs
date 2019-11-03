using NUnit.Framework;

namespace BakedInject.Tests
{
    public class ContainerTestsBase
    {
        protected Container container;

        [SetUp]
        public void ContainerSetup()
        {
            container = new Container();
        }
    }
}