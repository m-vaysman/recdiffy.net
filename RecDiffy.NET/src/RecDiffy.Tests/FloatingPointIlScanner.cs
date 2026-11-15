using System.Reflection;
using System.Reflection.Emit;

namespace RecDiffy.Tests;

// #361: Finds floating-point types in reflected metadata; the IL scanner and the member gate share it.
internal static class FloatingPointTypes
{
    // Types whose arithmetic is IEEE 754 floating point, by full name. A generic type is listed by its definition.
    private static readonly IReadOnlyDictionary<string, string> ByName = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Half"] = "Half",
        ["System.Runtime.InteropServices.NFloat"] = "NFloat",
        ["System.Numerics.Complex"] = "Complex",
        ["System.Numerics.Vector2"] = "Vector2",
        ["System.Numerics.Vector3"] = "Vector3",
        ["System.Numerics.Vector4"] = "Vector4",
        ["System.Numerics.Vector"] = "Vector",
        ["System.Numerics.Vector`1"] = "Vector<T>",
        ["System.Numerics.Matrix3x2"] = "Matrix3x2",
        ["System.Numerics.Matrix4x4"] = "Matrix4x4",
        ["System.Numerics.Plane"] = "Plane",
        ["System.Numerics.Quaternion"] = "Quaternion",
        ["System.Runtime.Intrinsics.Vector64"] = "Vector64",
        ["System.Runtime.Intrinsics.Vector64`1"] = "Vector64<T>",
        ["System.Runtime.Intrinsics.Vector128"] = "Vector128",
        ["System.Runtime.Intrinsics.Vector128`1"] = "Vector128<T>",
        ["System.Runtime.Intrinsics.Vector256"] = "Vector256",
        ["System.Runtime.Intrinsics.Vector256`1"] = "Vector256<T>",
        ["System.Runtime.Intrinsics.Vector512"] = "Vector512",
        ["System.Runtime.Intrinsics.Vector512`1"] = "Vector512<T>",
    };

    // Every floating-point type inside this type (an array element, a generic argument, a pointer target).
    internal static IEnumerable<string> FindAll(Type? type) => type switch
    {
        null => [],
        { IsByRef: true } or { IsArray: true } or { IsPointer: true } => FindAll(type.GetElementType()),
        { IsFunctionPointer: true } => AllIn([type.GetFunctionPointerReturnType(), .. type.GetFunctionPointerParameterTypes()]),
        { IsGenericParameter: true } => [],
        { IsGenericType: true } => ByName.TryGetValue(type.GetGenericTypeDefinition().FullName ?? "", out var generic)
            ? [generic]
            : AllIn(type.GetGenericArguments()),
        _ => ByName.TryGetValue(type.FullName ?? "", out var plain) ? [plain] : [],
    };

    internal static string? Find(Type? type) => FindAll(type).FirstOrDefault();

    internal static IEnumerable<string> AllIn(IEnumerable<Type?> types) => types.SelectMany(FindAll).Distinct(StringComparer.Ordinal);

    internal static string? FirstOf(IEnumerable<Type?> types) => AllIn(types).FirstOrDefault();

    // A key that names one method or constructor exactly: namespace-qualified type, name, parameter types.
    internal static string MethodKey(MethodBase method) => Signature(method) + ConversionReturn(method);

    // The method key plus the return type, so a change of return type is a different member.
    internal static string MemberKey(MethodBase method) => Signature(method) + ReturnSuffix(method);

    internal static string FieldKey(FieldInfo field) => $"{DeclaringName(field.DeclaringType)}.{field.Name}";

    internal static string FieldMemberKey(FieldInfo field) => $"{FieldKey(field)} : {field.FieldType}";

    private static string Signature(MethodBase method) =>
        $"{DeclaringName(method.DeclaringType)}.{method.Name}{GenericNames(method)}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.ToString()))})";

    // A constructed generic type is named without assembly versions, so a key is the same on every framework.
    private static string DeclaringName(Type? type) =>
        type is { IsGenericType: true, IsGenericTypeDefinition: false } ? type.ToString() : type?.FullName ?? "";

    // The conversion operators differ only by return type, so their key carries it.
    private static string ConversionReturn(MethodBase method) =>
        method is MethodInfo { Name: "op_Explicit" or "op_Implicit" } ? ReturnSuffix(method) : "";

    private static string ReturnSuffix(MethodBase method) =>
        method is MethodInfo info ? " : " + info.ReturnType.ToString() : "";

    private static string GenericNames(MethodBase method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(argument => argument.Name)) + ">" : "";
}

internal enum FindingKind
{
    Use,
    Local,
    Door,
    Unresolved,
}

// One floating-point use in a method body. A Local finding differs between Debug and Release builds, so the gate does not pin it.
// A Door finding is a call to a conversion door; Unresolved marks a token the scanner could not read and is never allowlisted.
internal sealed record FloatingPointFinding(string Method, string What, FindingKind Kind = FindingKind.Use);

// #361: Reads the IL of every method and constructor body and reports floating-point constants, conversions, locals and members.
internal static class FloatingPointIlScanner
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Opcodes that only ever act on a floating-point value.
    private static readonly HashSet<string> FloatingOpCodes = new(StringComparer.Ordinal)
    {
        "ldc.r4", "ldc.r8", "conv.r4", "conv.r8", "conv.r.un", "ckfinite",
        "ldelem.r4", "ldelem.r8", "stelem.r4", "stelem.r8", "ldind.r4", "ldind.r8", "stind.r4", "stind.r8",
    };

    // Opcode value (the two-byte forms start with 0xFE) to its description.
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeTable = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private sealed record Instruction(OpCode Op, int OperandOffset);

    internal static IReadOnlyList<FloatingPointFinding> Scan(Assembly assembly, IReadOnlySet<string> doors) => Scan(assembly.GetTypes(), doors);

    // Every method, constructor and static constructor of every type, nested and compiler-generated types included.
    // A door is a method that turns a boxed floating-point value into a decimal with no floating-point type in its signature.
    internal static IReadOnlyList<FloatingPointFinding> Scan(IEnumerable<Type> types, IReadOnlySet<string> doors) =>
    [
        .. Methods(types).SelectMany(method => Inspect(method, doors)),
    ];

    // The key of every method and constructor of the types, to check that an allowlist entry names a real one.
    internal static IReadOnlySet<string> MethodKeys(IEnumerable<Type> types) =>
        Methods(types).Select(FloatingPointTypes.MethodKey).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<MethodBase> Methods(IEnumerable<Type> types) =>
        types.SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)));

    private static IEnumerable<FloatingPointFinding> Inspect(MethodBase method, IReadOnlySet<string> doors)
    {
        var body = method.GetMethodBody();
        if (body is null)
            return [];

        var key = FloatingPointTypes.MethodKey(method);

        var locals = body.LocalVariables
            .SelectMany(local => FloatingPointTypes.FindAll(local.LocalType))
            .Select(found => new FloatingPointFinding(key, $"declares a local of type {found}", FindingKind.Local));

        var il = body.GetILAsByteArray() ?? [];
        var instructions = Decode(il).SelectMany(instruction => Findings(method, il, instruction, key, doors));

        return [.. locals, .. instructions];
    }

    private static IEnumerable<Instruction> Decode(byte[] il)
    {
        var offset = 0;
        while (offset < il.Length)
        {
            var first = il[offset];
            offset++;
            var value = first == 0xFE ? unchecked((short)(0xFE00 | il[offset++])) : first;
            var op = OpCodeTable[value];
            yield return new Instruction(op, offset);
            offset += OperandSize(op, il, offset);
        }
    }

    private static int OperandSize(OpCode op, byte[] il, int offset) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
        _ => 4,
    };

    private static IEnumerable<FloatingPointFinding> Findings(MethodBase method, byte[] il, Instruction instruction, string key, IReadOnlySet<string> doors)
    {
        var op = instruction.Op;

        var constant = op.Name switch
        {
            "ldc.r4" => $" {BitConverter.ToSingle(il, instruction.OperandOffset).ToInvariant()}",
            "ldc.r8" => $" {BitConverter.ToDouble(il, instruction.OperandOffset).ToInvariant()}",
            _ => "",
        };

        var byOpCode = FloatingOpCodes.Contains(op.Name!)
            ? [new FloatingPointFinding(key, $"uses the floating-point opcode {op.Name}{constant}")]
            : Array.Empty<FloatingPointFinding>();

        var byOperand = op.OperandType switch
        {
            OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType =>
                ByMember(method, op, BitConverter.ToInt32(il, instruction.OperandOffset), key, doors),
            OperandType.InlineSig => [new FloatingPointFinding(key, $"{op.Name} has a signature the scanner cannot read", FindingKind.Unresolved)],
            _ => [],
        };

        return [.. byOpCode, .. byOperand];
    }

    private static FloatingPointFinding[] ByMember(MethodBase method, OpCode op, int token, string key, IReadOnlySet<string> doors) =>
        Resolve(method, token) switch
        {
            null => [new FloatingPointFinding(key, $"{op.Name} token 0x{token.ToInvariant("x8")} cannot be resolved", FindingKind.Unresolved)],
            MethodBase door when doors.Contains(FloatingPointTypes.MethodKey(door)) =>
                [new FloatingPointFinding(key, $"{Verb(op)} {Describe(door)}", FindingKind.Door)],
            { } member when FloatingPointTypesIn(member).Any() || IsRuntimeConversion(member) =>
                [new FloatingPointFinding(key, $"{Verb(op)} {Describe(member)}")],
            _ => [],
        };

    // Resolves with the declaring type's and the method's generic arguments; null when the token cannot be read.
    private static MemberInfo? Resolve(MethodBase method, int token)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        try
        {
            return method.Module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or BadImageFormatException or TypeLoadException or NotSupportedException or MissingMemberException)
        {
            return null;
        }
    }

    // Every floating-point type in the member's signature, declaring type or generic arguments.
    private static IEnumerable<string> FloatingPointTypesIn(MemberInfo member) => member switch
    {
        MethodInfo method => FloatingPointTypes.AllIn(
            [method.DeclaringType, method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType), .. method.GetGenericArguments()]),
        ConstructorInfo constructor => FloatingPointTypes.AllIn(
            [constructor.DeclaringType, .. constructor.GetParameters().Select(parameter => parameter.ParameterType)]),
        FieldInfo field => FloatingPointTypes.AllIn([field.DeclaringType, field.FieldType]),
        Type type => FloatingPointTypes.FindAll(type),
        _ => [],
    };

    // Runtime conversions that turn a boxed double or float into a decimal with no floating-point token in the code:
    // Convert.ToDecimal(object, ...), Convert.ChangeType, the IConvertible methods and C# dynamic call sites.
    private static bool IsRuntimeConversion(MemberInfo member) =>
        member.DeclaringType is { } declaring
        && (declaring == typeof(IConvertible)
            || declaring.Namespace is "Microsoft.CSharp.RuntimeBinder"
            || declaring.FullName?.StartsWith("System.Runtime.CompilerServices.CallSite", StringComparison.Ordinal) is true
            || (declaring == typeof(Convert) && member is MethodBase method && ConvertsAnObject(method)));

    private static bool ConvertsAnObject(MethodBase method) =>
        method.Name is "ChangeType"
        || (method.Name is "ToDecimal" && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(object));

    private static string Describe(MemberInfo member) => member switch
    {
        MethodBase method => FloatingPointTypes.MethodKey(method),
        FieldInfo field => FloatingPointTypes.FieldKey(field),
        _ => member.ToString() ?? member.Name,
    };

    private static string Verb(OpCode op) => op.Name switch
    {
        "call" or "callvirt" => "calls",
        "newobj" => "constructs",
        "ldfld" or "ldsfld" or "ldflda" or "ldsflda" => "reads field",
        "stfld" or "stsfld" => "writes field",
        "ldftn" or "ldvirtftn" => "takes the address of",
        _ => op.Name ?? "uses",
    };
}
