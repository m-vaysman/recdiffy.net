using System.Globalization;
using System.Reflection;

namespace RecDiffy.ApiBaseline;

// The public surface of an assembly as text: one line for every public or protected type and member, sorted ordinally.
// A line holds the kind, the accessibility, static or instance, and the full signature: parameter names and types,
// nullable annotations, default values, generic constraints, enum values, the base type and the interfaces.
//
// This file is kept byte for byte the same in RecDiffy.Tests, RecDiffy.IO.Tests and RecDiffy.Excel.Tests, because each
// baseline test lives in a test project that already references its library. PublicApiBaselineTests in RecDiffy.Tests
// fails when the copies differ.
internal static class PublicApiSurface
{
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private const int ListedLines = 40;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["Boolean"] = "bool",
        ["Byte"] = "byte",
        ["Char"] = "char",
        ["Decimal"] = "decimal",
        ["Double"] = "double",
        ["Int16"] = "short",
        ["Int32"] = "int",
        ["Int64"] = "long",
        ["Object"] = "object",
        ["SByte"] = "sbyte",
        ["Single"] = "float",
        ["String"] = "string",
        ["UInt16"] = "ushort",
        ["UInt32"] = "uint",
        ["UInt64"] = "ulong",
        ["Void"] = "void",
    };

    // Only attributes a caller can see the effect of. The compiler's own bookkeeping attributes are read where they
    // change a signature (nullability, tuple names, params, extension methods) and are not listed.
    private static readonly HashSet<string> ShownAttributes = new(StringComparer.Ordinal)
    {
        "AllowNullAttribute",
        "DisallowNullAttribute",
        "DoesNotReturnAttribute",
        "EditorBrowsableAttribute",
        "EnumeratorCancellationAttribute",
        "ExperimentalAttribute",
        "FlagsAttribute",
        "MaybeNullAttribute",
        "MaybeNullWhenAttribute",
        "NotNullAttribute",
        "NotNullIfNotNullAttribute",
        "NotNullWhenAttribute",
        "ObsoleteAttribute",
        "RequiresDynamicCodeAttribute",
        "RequiresUnreferencedCodeAttribute",
        "SetsRequiredMembersAttribute",
    };

    public static string BaselinePath(string testProject, string assemblyName) =>
        Path.Combine(RepoRoot(), "RecDiffy.NET", "src", testProject, "ApiBaseline", assemblyName + ".txt");

    public static string ActualPath(string assemblyName) =>
        Path.Combine(AppContext.BaseDirectory, "ApiBaseline", assemblyName + ".actual.txt");

    // Null when the assembly's surface equals its committed baseline. Otherwise the message a failing test shows.
    public static string? Check(Assembly assembly, string testProject)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(testProject);
        var name = assembly.GetName().Name ?? "";
        return Check(name, Describe(assembly), BaselinePath(testProject, name), ActualPath(name));
    }

    // The baseline is only ever read. On a difference the actual surface is written under the test output folder, and
    // a person copies it over the baseline to accept the change.
    public static string? Check(string assemblyName, IReadOnlyList<string> actual, string baselinePath, string actualPath)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(baselinePath);
        ArgumentNullException.ThrowIfNull(actualPath);
        string[] repeated = [.. actual.GroupBy(line => line, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];
        if (repeated.Length > 0)
            return $"The public API text of {assemblyName} repeats a line, so a removed member could go unseen. Make PublicApiSurface tell these apart:{Listed("  ", repeated)}";
        var baseline = File.Exists(baselinePath) ? Read(baselinePath) : null;
        var difference = Compare(baseline ?? [], actual);
        if (baseline is not null && difference.IsEmpty)
            return null;
        Directory.CreateDirectory(Path.GetDirectoryName(actualPath) ?? ".");
        File.WriteAllText(actualPath, Render(assemblyName, actual));
        var accept = string.Join('\n',
            "Accept it by hand: review the surface this run wrote to",
            $"  {actualPath}",
            "copy that file over",
            $"  {baselinePath}",
            "and commit the baseline with the change. Nothing rewrites the baseline for you.");
        return baseline is null
            ? $"The public API of {assemblyName} has no baseline: {baselinePath} is missing.\n{accept}"
            : string.Join('\n',
                $"The public API of {assemblyName} differs from its baseline: {Count(difference.Removed.Count)} of the baseline {(difference.Removed.Count == 1 ? "is" : "are")} gone or changed, and {Count(difference.Added.Count)} {(difference.Added.Count == 1 ? "is" : "are")} new.",
                "A line marked - is in the baseline and not in the assembly. A line marked + is in the assembly and not in the baseline. A changed signature shows as one of each."
                    + Listed("- ", difference.Removed)
                    + Listed("+ ", difference.Added),
                "If the change is not intended, restore the member. If it is intended:",
                accept);
    }

    public static ApiDifference Compare(IReadOnlyList<string> baseline, IReadOnlyList<string> actual)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(actual);
        return new(
            [.. baseline.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. actual.Except(baseline, StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    // A line that starts with # is a comment, so the file can say what it is.
    public static IReadOnlyList<string> Read(string path) =>
        [.. File.ReadAllLines(path).Where(line => line.Length > 0 && line.StartsWith('#') is false)];

    public static string Render(string assemblyName, IReadOnlyList<string> lines) =>
        string.Join('\n',
        [
            $"# Public API baseline of {assemblyName}: one line for every public or protected type and member, sorted ordinally.",
            "# PublicApiBaselineTests compares the built assembly with this file on net8.0 and net10.0, and fails on any difference.",
            "# To accept an intended change, copy the .actual.txt file the failing test names over this file and commit it.",
            .. lines,
            "",
        ]);

    public static IReadOnlyList<string> Describe(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return [.. assembly.GetTypes().Where(Visible).SelectMany(type => TypeLines(type)).Order(StringComparer.Ordinal)];
    }

    private static string Count(int lines) => $"{lines.ToString(CultureInfo.InvariantCulture)} line{(lines == 1 ? "" : "s")}";

    private static string Listed(string mark, IReadOnlyList<string> lines) =>
        string.Concat(lines.Take(ListedLines).Select(line => $"\n{mark}{line}"))
        + (lines.Count > ListedLines ? $"\n{mark}... and {(lines.Count - ListedLines).ToString(CultureInfo.InvariantCulture)} more" : "");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found above the test output folder.");
    }

    private static bool Visible(Type type) =>
        type.IsNested
            ? (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem) && type.DeclaringType is { } outer && Visible(outer)
            : type.IsPublic;

    private static IEnumerable<string> TypeLines(Type type)
    {
        var name = Named(type, index => type.GetGenericArguments()[index].Name);
        return [$"{name} :: {Header(type)}", .. Members(type).Select(member => $"{name} :: {member}")];
    }

    private static string Header(Type type)
    {
        var access = type switch
        {
            { IsNested: false } or { IsNestedPublic: true } => "public",
            { IsNestedFamily: true } => "protected",
            _ => "protected internal",
        };
        var attributes = Attributes(type.GetCustomAttributesData());
        if (IsDelegate(type) && type.GetMethod("Invoke") is { } invoke)
            return $"type {attributes}{access} delegate {Returned(invoke)} Invoke({Parameters(invoke)}){Constraints(type.GetGenericArguments())}";
        var kind = type switch
        {
            { IsEnum: true } => $"enum : {TypeName(Enum.GetUnderlyingType(type))}",
            { IsInterface: true } => "interface",
            { IsValueType: true } => $"{(Has(type.GetCustomAttributesData(), "IsReadOnlyAttribute") ? "readonly " : "")}{(type.IsByRefLike ? "ref " : "")}struct",
            { IsAbstract: true, IsSealed: true } => "static class",
            { IsAbstract: true } => "abstract class",
            { IsSealed: true } => "sealed class",
            _ => "class",
        };
        var variance = string.Concat(type.GetGenericArguments().Where(argument => argument.IsGenericParameter).Select(Variance));
        var inherited = type.BaseType is { } parent && parent != typeof(object) && parent != typeof(ValueType) && parent != typeof(Enum)
            ? $" extends {TypeName(parent)}"
            : "";
        // Only the interfaces this type adds to its base type's, so a change in a framework base type does not move the line.
        var added = type.GetInterfaces().Except(type.BaseType?.GetInterfaces() ?? []).Select(contract => TypeName(contract)).Order(StringComparer.Ordinal).ToArray();
        var implemented = added.Length == 0 ? "" : $" implements {string.Join(", ", added)}";
        return $"type {attributes}{access} {kind}{variance}{inherited}{implemented}{Constraints(type.GetGenericArguments())}{TupleNames(type.GetCustomAttributesData())}";
    }

    private static string Variance(Type parameter) =>
        (parameter.GenericParameterAttributes & GenericParameterAttributes.VarianceMask) switch
        {
            GenericParameterAttributes.Covariant => $" (out {parameter.Name})",
            GenericParameterAttributes.Contravariant => $" (in {parameter.Name})",
            _ => "",
        };

    private static bool IsDelegate(Type type) => typeof(MulticastDelegate).IsAssignableFrom(type.BaseType);

    private static IEnumerable<string> Members(Type type)
    {
        if (IsDelegate(type))
            return [];
        if (type.IsEnum)
            return type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => $"enum value {field.Name} = {Literal(field.GetRawConstantValue())}");
        var properties = type.GetProperties(Declared);
        var events = type.GetEvents(Declared);
        var accessors = properties.SelectMany(property => property.GetAccessors(true))
            .Concat(events.SelectMany(raised => new[] { raised.AddMethod, raised.RemoveMethod, raised.RaiseMethod }).OfType<MethodInfo>())
            .ToHashSet();
        return
        [
            .. type.GetFields(Declared).Select(field => Field(field)).OfType<string>(),
            .. type.GetConstructors(Declared).Select(constructor => Constructor(constructor)).OfType<string>(),
            .. properties.Select(property => Property(property)).OfType<string>(),
            .. events.Select(raised => Event(raised)).OfType<string>(),
            .. type.GetMethods(Declared).Where(method => accessors.Contains(method) is false).Select(method => Method(method)).OfType<string>(),
        ];
    }

    private static string? Field(FieldInfo field)
    {
        var access = field switch
        {
            { IsPublic: true } => "public",
            { IsFamily: true } => "protected",
            { IsFamilyOrAssembly: true } => "protected internal",
            _ => null,
        };
        var modifiers = field switch
        {
            { IsLiteral: true } => "const ",
            { IsStatic: true, IsInitOnly: true } => "static readonly ",
            { IsStatic: true } => "static ",
            { IsInitOnly: true } => "readonly ",
            _ => "",
        };
        var value = field.IsLiteral ? $" = {Literal(field.GetRawConstantValue())}" : "";
        return access is null
            ? null
            : $"field {Attributes(field.GetCustomAttributesData())}{access} {Required(field)}{modifiers}{TypeName(field.FieldType, Annotations.Of(field.GetCustomAttributesData(), null, field.DeclaringType))} {field.Name}{value}{TupleNames(field.GetCustomAttributesData())}";
    }

    private static string? Constructor(ConstructorInfo constructor) =>
        Access(constructor) is { } access && constructor.IsStatic is false
            ? $"constructor {Attributes(constructor.GetCustomAttributesData())}{access} ({Parameters(constructor)})"
            : null;

    private static string? Property(PropertyInfo property)
    {
        (MethodInfo Method, string Access, string Word)[] accessors =
        [
            .. new[] { (Method: property.GetMethod, Word: "get"), (Method: property.SetMethod, Word: IsInit(property) ? "init" : "set") }
                .Where(accessor => accessor.Method is not null && Access(accessor.Method) is not null)
                .Select(accessor => (accessor.Method!, Access(accessor.Method!)!, accessor.Word)),
        ];
        if (accessors.Length == 0)
            return null;
        var widest = accessors.MinBy(accessor => Rank(accessor.Access));
        var body = string.Join(" ", accessors.Select(accessor => $"{(accessor.Access == widest.Access ? "" : accessor.Access + " ")}{accessor.Word};"));
        var index = property.GetIndexParameters();
        var name = index.Length == 0 ? property.Name : $"{property.Name}[{string.Join(", ", index.Select(parameter => Parameter(parameter, false)))}]";
        return $"property {Attributes(property.GetCustomAttributesData())}{widest.Access} {Required(property)}{Modifiers(widest.Method)}{TypeName(property.PropertyType, Annotations.Of(property.GetCustomAttributesData(), null, property.DeclaringType))} {name} {{ {body} }}{TupleNames(property.GetCustomAttributesData())}";
    }

    private static string? Event(EventInfo raised) =>
        raised.AddMethod is { } add && Access(add) is { } access && raised.EventHandlerType is { } handler
            ? $"event {Attributes(raised.GetCustomAttributesData())}{access} {Modifiers(add)}{TypeName(handler, Annotations.Of(raised.GetCustomAttributesData(), null, raised.DeclaringType))} {raised.Name}"
            : null;

    private static string? Method(MethodInfo method)
    {
        var generic = method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : "";
        var constraints = method.IsGenericMethodDefinition ? Constraints(method.GetGenericArguments()) : "";
        return Access(method) is { } access
            ? $"method {Attributes(method.GetCustomAttributesData())}{access} {Modifiers(method)}{Returned(method)} {method.Name}{generic}({Parameters(method)}){constraints}{TupleNames(method.ReturnParameter.GetCustomAttributesData())}"
            : null;
    }

    private static string Returned(MethodInfo method) =>
        $"{Attributes(method.ReturnParameter.GetCustomAttributesData())}{(method.ReturnType.IsByRef ? "ref " : "")}{TypeName(method.ReturnType, Annotations.Of(method.ReturnParameter.GetCustomAttributesData(), method, method.DeclaringType))}";

    private static string Parameters(MethodBase method)
    {
        var extension = Has(method.GetCustomAttributesData(), "ExtensionAttribute");
        return string.Join(", ", method.GetParameters().Select(parameter => Parameter(parameter, extension)));
    }

    private static string Parameter(ParameterInfo parameter, bool extension)
    {
        var attributes = parameter.GetCustomAttributesData();
        var receiver = extension && parameter.Position == 0 ? "this " : "";
        var passing = parameter switch
        {
            { ParameterType.IsByRef: true, IsOut: true } => "out ",
            { ParameterType.IsByRef: true } when Has(attributes, "RequiresLocationAttribute") => "ref readonly ",
            { ParameterType.IsByRef: true, IsIn: true } => "in ",
            { ParameterType.IsByRef: true } => "ref ",
            _ when Has(attributes, "ParamArrayAttribute") || Has(attributes, "ParamCollectionAttribute") => "params ",
            _ => "",
        };
        var value = parameter.HasDefaultValue ? $" = {Default(parameter)}" : "";
        return $"{Attributes(attributes)}{receiver}{passing}{TypeName(parameter.ParameterType, Annotations.Of(attributes, Owner(parameter), parameter.Member.DeclaringType))} {parameter.Name}{value}{TupleNames(attributes)}";
    }

    private static string Default(ParameterInfo parameter) =>
        parameter.RawDefaultValue switch
        {
            null when parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null => "default",
            var value => Literal(value),
        };

    private static string? Access(MethodBase method) =>
        method switch
        {
            { IsPublic: true } => "public",
            { IsFamily: true } => "protected",
            { IsFamilyOrAssembly: true } => "protected internal",
            _ => null,
        };

    private static int Rank(string access) =>
        access switch
        {
            "public" => 0,
            "protected internal" => 1,
            _ => 2,
        };

    // An implicit interface implementation is virtual and final in metadata, and plain in C#.
    private static string Modifiers(MethodInfo method) =>
        method switch
        {
            { IsStatic: true, IsAbstract: true } => "static abstract ",
            { IsStatic: true, IsVirtual: true } => "static virtual ",
            { IsStatic: true } => "static ",
            { IsAbstract: true, DeclaringType.IsInterface: true } => "",
            { IsAbstract: true } => "abstract ",
            { IsVirtual: true, IsFinal: false } => Overrides(method) ? "override " : "virtual ",
            { IsVirtual: true } => Overrides(method) ? "sealed override " : "",
            _ => "",
        };

    private static bool Overrides(MethodInfo method) => method.GetBaseDefinition().DeclaringType != method.DeclaringType;

    private static bool IsInit(PropertyInfo property) =>
        property.SetMethod is { } setter && setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.Name == "IsExternalInit");

    private static string Required(MemberInfo member) =>
        Has(member.GetCustomAttributesData(), "RequiredMemberAttribute") ? "required " : "";

    private static string Constraints(IEnumerable<Type> arguments) =>
        string.Concat(arguments.Where(argument => argument.IsGenericParameter).Select(Constraint));

    private static string Constraint(Type parameter)
    {
        var flags = parameter.GenericParameterAttributes;
        var valueType = flags.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);
        var referenceType = flags.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint);
        var types = parameter.GetGenericParameterConstraints().Where(type => type != typeof(ValueType)).Select(type => TypeName(type)).Order(StringComparer.Ordinal).ToArray();
        string[] parts =
        [
            .. referenceType ? [NullableFlag(parameter) is 2 ? "class?" : "class"] : Array.Empty<string>(),
            .. valueType ? [Has(parameter.GetCustomAttributesData(), "IsUnmanagedAttribute") ? "unmanaged" : "struct"] : Array.Empty<string>(),
            .. referenceType is false && valueType is false && types.Length == 0 && NullableFlag(parameter) is 1 ? ["notnull"] : Array.Empty<string>(),
            .. types,
            .. flags.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && valueType is false ? ["new()"] : Array.Empty<string>(),
            // GenericParameterAttributes.AllowByRefLike, which net8.0 does not name.
            .. ((int)flags & 0x20) != 0 ? ["allows ref struct"] : Array.Empty<string>(),
        ];
        return parts.Length == 0 ? "" : $" where {parameter.Name} : {string.Join(", ", parts)}";
    }

    // The compiler writes a type parameter's nullability on the parameter, or once on the method or type that declares it.
    private static byte NullableFlag(Type parameter) =>
        Annotations.Flag(parameter.GetCustomAttributesData(), "NullableAttribute")
        ?? Annotations.Context(parameter.DeclaringMethod, parameter.DeclaringType);

    // An indexer's parameters belong to the property, and their annotations sit on its accessor.
    private static MethodBase? Owner(ParameterInfo parameter) =>
        parameter.Member switch
        {
            MethodBase method => method,
            PropertyInfo property => property.GetMethod ?? property.SetMethod,
            _ => null,
        };

    private static bool Has(IEnumerable<CustomAttributeData> attributes, string name) =>
        attributes.Any(attribute => attribute.AttributeType.Name == name);

    private static string Attributes(IEnumerable<CustomAttributeData> attributes) =>
        string.Concat(attributes.Where(attribute => ShownAttributes.Contains(attribute.AttributeType.Name)).Select(Attribute).Order(StringComparer.Ordinal).Select(text => $"[{text}] "));

    private static string Attribute(CustomAttributeData attribute)
    {
        var name = attribute.AttributeType.Name[..^"Attribute".Length];
        string[] arguments =
        [
            .. attribute.ConstructorArguments.Select(argument => Literal(argument.Value)),
            .. attribute.NamedArguments.OrderBy(argument => argument.MemberName, StringComparer.Ordinal).Select(argument => $"{argument.MemberName} = {Literal(argument.TypedValue.Value)}"),
        ];
        return arguments.Length == 0 ? name : $"{name}({string.Join(", ", arguments)})";
    }

    private static string TupleNames(IEnumerable<CustomAttributeData> attributes) =>
        attributes.FirstOrDefault(attribute => attribute.AttributeType.Name == "TupleElementNamesAttribute") is { ConstructorArguments: [{ Value: IEnumerable<CustomAttributeTypedArgument> names }] }
            ? $" [tuple names: {string.Join(", ", names.Select(name => name.Value as string ?? "_"))}]"
            : "";

    private static string Literal(object? value) =>
        value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char letter => $"'{letter}'",
            bool flag => flag ? "true" : "false",
            Type type => $"typeof({TypeName(type)})",
            IEnumerable<CustomAttributeTypedArgument> items => $"[{string.Join(", ", items.Select(item => Literal(item.Value)))}]",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

    // The annotations are read in the order the compiler writes them: a type, then its generic arguments, left to right.
    private static string TypeName(Type type, Annotations? annotations = null)
    {
        if (type.IsByRef)
            return TypeName(type.GetElementType()!, annotations);
        if (type.IsPointer)
            return $"{TypeName(type.GetElementType()!, annotations)}*";
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return $"{TypeName(underlying, annotations)}?";
        // A value type that is not generic has no annotation. A generic one has an annotation that says nothing.
        var reference = type.IsGenericParameter || type.IsValueType is false;
        var flag = reference || type.IsGenericType ? annotations?.Next() : null;
        var mark = reference && flag is 2 ? "?" : "";
        if (type.IsArray)
            return $"{TypeName(type.GetElementType()!, annotations)}[{new string(',', type.GetArrayRank() - 1)}]{mark}";
        if (type.IsGenericParameter)
            return $"{type.Name}{mark}";
        string[] arguments = [.. type.GetGenericArguments().Select(argument => TypeName(argument, annotations))];
        return $"{Named(type, index => arguments[index])}{mark}";
    }

    // Namespace, then the type and the types it is nested in, each with its own generic arguments.
    private static string Named(Type type, Func<int, string> argument)
    {
        if (type.IsNested is false && type.Namespace == "System" && Aliases.TryGetValue(type.Name, out var alias))
            return alias;
        Type[] chain = [.. Outward(type.IsGenericType ? type.GetGenericTypeDefinition() : type).Reverse()];
        int[] arity = [.. chain.Select(link => link.GetGenericArguments().Length)];
        var names = chain.Select((link, position) =>
        {
            var first = position == 0 ? 0 : arity[position - 1];
            var bare = link.Name.IndexOf('`', StringComparison.Ordinal) is var tick and >= 0 ? link.Name[..tick] : link.Name;
            return arity[position] == first ? bare : $"{bare}<{string.Join(", ", Enumerable.Range(first, arity[position] - first).Select(argument))}>";
        });
        return $"{(type.Namespace is { Length: > 0 } space ? space + "." : "")}{string.Join(".", names)}";
    }

    private static IEnumerable<Type> Outward(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            yield return current;
    }
}

internal sealed record ApiDifference(IReadOnlyList<string> Removed, IReadOnlyList<string> Added)
{
    public bool IsEmpty => Removed.Count == 0 && Added.Count == 0;
}

// The nullable annotations of one signature position, as the compiler stores them: one flag for the whole type, or one
// flag for each part of it, or the flag of the enclosing method or type. 1 is not null, 2 is nullable, 0 is unannotated.
// Reading the attributes here, and not through the runtime's NullabilityInfoContext, gives the same text on net8.0 and net10.0.
internal sealed class Annotations
{
    private readonly byte[]? _each;
    private readonly byte _all;
    private int _read;

    private Annotations(byte[]? each, byte all) => (_each, _all) = (each, all);

    public static Annotations Of(IEnumerable<CustomAttributeData> site, MethodBase? method, Type? declaring) =>
        site.FirstOrDefault(attribute => attribute.AttributeType.Name == "NullableAttribute") switch
        {
            { ConstructorArguments: [{ Value: byte all }] } => new(null, all),
            { ConstructorArguments: [{ Value: IEnumerable<CustomAttributeTypedArgument> each }] } => new([.. each.Select(flag => flag.Value).OfType<byte>()], 0),
            _ => new(null, Context(method, declaring)),
        };

    public static byte Context(MethodBase? method, Type? declaring) =>
        (method is null ? [] : new[] { method.GetCustomAttributesData() })
            .Concat(Enclosing(declaring).Select(type => type.GetCustomAttributesData()))
            .Select(attributes => Flag(attributes, "NullableContextAttribute"))
            .FirstOrDefault(flag => flag is not null) ?? 0;

    public static byte? Flag(IEnumerable<CustomAttributeData> attributes, string name) =>
        attributes.FirstOrDefault(attribute => attribute.AttributeType.Name == name) is { ConstructorArguments: [var argument] }
            ? argument.Value switch
            {
                byte flag => flag,
                IEnumerable<CustomAttributeTypedArgument> flags => flags.Select(item => item.Value).OfType<byte>().Cast<byte?>().FirstOrDefault(),
                _ => null,
            }
            : null;

    public byte Next() => _each is null ? _all : _read < _each.Length ? _each[_read++] : (byte)0;

    private static IEnumerable<Type> Enclosing(Type? type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            yield return current;
    }
}
