This is a tiny dependency injection framework for Unity, that uses Mono.Cecil to weave generated assemblies and add baked factories and injection methods. This allows injection to work without using reflection making it faster and able to work in AOT compilation, like IL2CPP.

This is still experimental and should not be used in production.

# How it works
Using Mono.Cecil it hooks up to unity's `CompilationPipeline` . When an assembly file is created all classes that have a constructor or fields marked with the `Inject` attribute will have new methods injected (weaved) that resolve all field and parameters using the Dependency injection container. 
Each assembly also has a generated master method that has the attribute `InitializeOnLoadMethodAttribute` or `RuntimeInitializeOnLoadMethodAttribute`. That method is then called by unity and all weaved methods are registered in the `ContainerDatabase` and later utilized.

# Installation

This uses Unity's package system. The add it you a project it should be added to a folder `BakedInject` in the Packages folder.

Alternative editing the package manifest located in `Packages` folder like this:
``` json
{
    "dependencies": {
        "baked-inject": "https://github.com/simeonradivoev/BakedInject.git"
    }
}
```

# Usage
The API is very similar to other dependency injection frameworks, with a few catches.
Check out [Zenject](https://github.com/modesttree/Zenject) or [Ninject](https://github.com/ninject/ninject)

Here is an example usage:

``` csharp

var container = new Container();

//bind instance
container.Bind("Test");

//waved classes can have their field and constructor injected.
container.Bind<BakedClass>().AsSingle();

//use with non weaved class with an empty constructor.
container.BindNew<NonBakedClass>().AsSingle();

//use for non weaved classes using a factory.
container
.Bind<INonBakedClass>()
.AsSingle()
.FromFactory(c => new NonBakedClass2(c.Resolve<string>()));

var bakedClass = container.Resolve<BakedClass>();
```

All weaved constructors and injection methods are registered in the `ContainerDatabase` class. It can also be used to manually register factories and injectors if one wants to.

# Catches:

### Constructors
To instantiate classes they need to have a constructor with the attribute [Inject]. Otherwise they need to be explicitly created by a factory method or passed by instance. Private and public constructors can be injected.

``` csharp
public class TestInjection
{
    public string test;

    [Inject]
    public TestInjection(string test)
    {
        this.test = test;
    }
}
```

### Fields
To inject a field, that field needs to have the [Inject] attribute. Private and public fields can be injected.
``` csharp
public class TestInjection
{
    [Inject]
    public string test;
}
```

#Notes
If you really, really, really want reflection it can be enabled by a script define `ENABLE_REFLECT_INJECTION`. Then reflection will be used as a fallback