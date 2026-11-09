# Registration, attributes and conversions

Before adding a registration pipeline, inspect what the project already has: package versions, target framework, `.dna` declarations and build properties, exported assemblies, and any registration code.

v1.9 builds extended registration into `ExcelDna.Integration`. The `ExcelDna.Registration` namespace remains, but the old separate `ExcelDna.Registration` package is no longer needed. Pick exactly one registration path per function.

## Excel-DNA's attributes in v1.9

These are all of the public attribute types in `ExcelDna.Integration` at tag `v1.9.0-Release`. The line numbers point into `Source/ExcelDna.Integration/ExcelAttributes.cs` unless another file is named.

| Attribute | Goes on | Purpose | Settings |
|---|---|---|---|
| `[ExcelFunction]` (line 14) | Method | A worksheet function (UDF) | `Name`, `Description`, `Category`, `HelpTopic`, `IsVolatile`, `IsHidden`, `IsExceptionSafe`, `IsMacroType`, `IsThreadSafe`, `IsClusterSafe`, `ExplicitRegistration`, `SuppressOverwriteError` |
| `[ExcelArgument]` (line 64) | Parameter | Describes a function argument | `Name`, `Description`, `AllowReference` (pass an `ExcelReference` instead of values) |
| `[ExcelCommand]` (line 113) | Method | A macro command: a menu entry, shortcut or callable command | `Name`, `Description`, `HelpTopic`, `ShortCut`, `MenuName`, `MenuText`, `IsExceptionSafe`, `ExplicitRegistration`, `SuppressOverwriteError` (`IsHidden` is obsolete for commands) |
| `[ExcelParameterConversion]` (line 143) | Method | A conversion applied to function parameters, discovered at startup | None |
| `[ExcelReturnConversion]` (line 152) | Method | A conversion applied to function return values, discovered at startup | None |
| `[ExcelFunctionExecutionHandlerSelector]` (line 161) | Method | Chooses an execution handler, for example for logging or timing, per function | None |
| `[ExcelFunctionProcessor]` (line 167) | Method | A custom step in the registration pipeline | None |
| `[ExcelHandle]` (line 89) | Parameter, return value, class or struct | Passes an object to and from Excel as an object handle | None |
| `[ExcelHandleExternal(typeof(T))]` (line 98) | Assembly, repeatable | Marks a type you don't own to be passed as an object handle | `Type` |
| `[ExcelAsyncFunction]` (`Registration/Attributes.cs`, line 14, namespace `ExcelDna.Registration`) | Method | An async worksheet function. It derives from `[ExcelFunction]`. | Those of `[ExcelFunction]`. Its constructor sets `ExplicitRegistration = true`. |

`ExcelDna.Integration` also defines `ExcelParamsArgumentAttribute`, which is internal and not for application code.

Things that look like attributes but aren't Excel-DNA attributes:

- **`IExcelAddIn`** is an interface, with `AutoOpen` and `AutoClose`.
- **`ExcelRibbon` and `ExcelComAddIn`** are base classes, and both are COM (see `SKILL.md`).
- **COM server and RTD server classes** use the standard .NET `[ComVisible]` and `[ProgId]` attributes.

## Automatic registration

In the v1.9 source, functions and commands are registered **before** the add-in's own `IExcelAddIn.AutoOpen()` runs (`DnaLibrary.cs:322-339`). Configuring conversions in `AutoOpen` can't change those registrations after the fact.

For automatic registration, expose discoverable public static one-argument methods:

- `[ExcelParameterConversion]`: from an Excel-friendly input to a domain type.
- `[ExcelReturnConversion]`: from a domain type to an Excel-friendly output.

Keep converters deterministic and cheap. Check the installed implementation before relying on converters that duplicate each other or depend on order.

Assembly-wide `ExplicitRegistration=true` skips discovering these attributes. `AssemblyLoader.cs:85-88` collects conversions only `if (!explicitRegistration)`.

## Explicit registration

Suppress automatic registration for the affected functions, build the conversion configuration, apply the transformations you need, and register once.

- **Two different scopes.** Per-function `ExplicitRegistration=true` and assembly-wide explicit registration aren't the same thing. `ExplicitExports` only narrows what's exported; it doesn't mean manual registration.
- **Build property:** v1.9 supports `ExcelAddInExplicitRegistration`. Inspect the generated `.dna` output.
- **Avoid double registration.** Filter discovery in mixed assemblies so unrelated automatic functions aren't registered twice.
- **Commands are separate.** Handle commands on their own when their automatic registration was suppressed.

A synchronous example, illustrative only:

```csharp
using System.Linq;
using ExcelDna.Integration;
using ExcelDna.Registration;

public sealed record InstrumentKey(string Value);

public sealed class AddIn : IExcelAddIn
{
    public void AutoOpen()
    {
        var conversions = new ParameterConversionConfiguration()
            .AddParameterConversion((string text) => new InstrumentKey(text))
            .AddReturnConversion((InstrumentKey key) => key.Value);

        ExcelRegistration.GetExcelFunctions()
            .Where(function => function.FunctionAttribute.Name == "Example.EchoKey")
            .ProcessParameterConversions(conversions)
            .RegisterFunctions();
    }

    public void AutoClose() { }
}

public static class Functions
{
    [ExcelFunction(Name = "Example.EchoKey", ExplicitRegistration = true)]
    public static InstrumentKey EchoKey(InstrumentKey key) => key;
}
```

This isn't a complete general-purpose pipeline. It has no production validation, and it doesn't include the async, params or nullable extensions. Compile it and exercise it against the project's exact packages.

## Conversion order

- **Parameters:** conversions are applied in reverse order to how they're identified. The source comment at `Registration/ParameterConversionRegistration.cs:48` says so. For a parameter going `string → A → B`, explicit registration lists `A → B` before `string → A`, because wrapping starts at the target parameter type.
- **Return values:** for `A → B → string`, list the conversions in forward order.
- **Nullable types:** place nullable conversions after the underlying conversions they use.
- **Don't lose the defaults.** A hand-written pipeline doesn't inherit all of the automatic defaults. Keep optional and default arguments, nullable behavior, async registration, params, handles and execution handlers deliberately.
- **Pipeline order:** the v1.9 pipeline applies parameter conversions before async wrapping, then params, handles, and execution handlers.
- **`ProcessFunctions()`** runs the built-in pipeline with the discovered configuration. Don't combine it blindly with duplicate manual steps.
- **No initialization calls:** don't add the obsolete `ExcelAsyncUtil.Initialize()` or `Uninitialize()` calls on v1.9.

## References and COM

Don't accept Office interop `Range` parameters under the no-COM rule. v1.9's automatic conversion from a range to a COM `Range` doesn't authorize using it.

When a function really needs a reference, take an `object` parameter marked `[ExcelArgument(AllowReference = true)]` and handle the `ExcelReference`. Otherwise, receive values.

## Cell conversion for range readers

The conversions registered here are also the single definition that range readers must use. See "Cell conversion is defined once, at startup" in `SKILL.md`.
