using System;

namespace BakedInject
{
    public class InjectAttribute : Attribute
    {
        public bool Optional { get; set; }
        public object Id { get; set; }
    }
}