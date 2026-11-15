using System.Reflection;
using System.Reflection.Emit;

namespace RecDiffy.Tests;

// #361: Finds floating-point types in reflected metadata; the IL scanner and the member gate share it.
internal static class FloatingPointTypes
{
    // The first floating-point type inside this type (an array element, a generic argument, a pointer target), or null.
    internal static string? Find(Type? type) => type switch
    {
        null => null,
        { IsByRef: true } or { IsArray: true } or { IsPointer: true } => Find(type.GetElementType()),
        { IsFunctionPointer: true } => FirstOf([type.GetFunctionPointerReturnType(), .. type.GetFunctionPointerParameterTypes()]),
        { IsGenericParameter: true } => null,
        { IsGenericType: true } => FirstOf(type.GetGenericArguments()),
        _ => type.FullName switch
        {
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Half" => "Half",
            _ => null,
        },
    };

    internal static string? FirstOf(IEnumerable<Type?> types) =>
        types.Select(Find).FirstOrDefault(found => found is not null);

    // A key that names one method or constructor exactly: namespace-qualified type, name, parameter types.
    internal static string MethodKey(MethodBase method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}{GenericNames(method)}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.ToString()))}){ConversionReturn(method)}";

    internal static string FieldKey(FieldInfo field) => $"{field.DeclaringType?.FullName}.{field.Name}";

    // The conversion operators differ only by return type, so their key carries it.
    private static string ConversionReturn(MethodBase method) =>
        method is MethodInfo { Name: "op_Explicit" or "op_Implicit" } conversion ? " : " + conversion.ReturnType.ToString() : "";

    private static string GenericNames(MethodBase method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(argument => argument.Name)) + ">" : "";
}

// One floating-point use in a method body; Unresolved marks a token the scanner could not read, which is never allowlisted.
internal sealed record FloatingPointFinding(string Method, string What, bool Unresolved = false);

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

    // Every method, constructor and static constructor of every type, nested and compiler-generated types included.
    internal static IReadOnlyList<FloatingPointFinding> Scan(Assembly assembly) => Scan(assembly.GetTypes());

    internal static IReadOnlyList<FloatingPointFinding> Scan(IEnumerable<Type> types) =>
    [
        .. types
            .SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            .SelectMany(Inspect),
    ];

    private static IEnumerable<FloatingPointFinding> Inspect(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null)
            return [];

        var key = FloatingPointTypes.MethodKey(method);

        var locals = body.LocalVariables
            .Select(local => FloatingPointTypes.Find(local.LocalType))
            .OfType<string>()
            .Select(found => new FloatingPointFinding(key, $"declares a local of type {found}"));

        var il = body.GetILAsByteArray() ?? [];
        var instructions = Decode(il).SelectMany(instruction => Findings(method, il, instruction, key));

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

    private static IEnumerable<FloatingPointFinding> Findings(MethodBase method, byte[] il, Instruction instruction, string key)
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
                ByMember(method, op, BitConverter.ToInt32(il, instruction.OperandOffset), key),
            OperandType.InlineSig => [new FloatingPointFinding(key, $"{op.Name} has a signature the scanner cannot read", Unresolved: true)],
            _ => [],
        };

        return [.. byOpCode, .. byOperand];
    }

    private static FloatingPointFinding[] ByMember(MethodBase method, OpCode op, int token, string key)
    {
        var resolved = Resolve(method, token);

        return resolved switch
        {
            null => [new FloatingPointFinding(key, $"{op.Name} token 0x{token.ToInvariant("x8")} cannot be resolved", Unresolved: true)],
            _ when FloatingIn(resolved) is not null => [new FloatingPointFinding(key, $"{Verb(op)} {Describe(resolved)}")],
            _ => [],
        };
    }

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

    // The first floating-point type in the member's signature, declaring type or generic arguments, or null.
    private static string? FloatingIn(MemberInfo member) => member switch
    {
        MethodInfo method => FloatingPointTypes.FirstOf(
            [method.DeclaringType, method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType), .. method.GetGenericArguments()]),
        ConstructorInfo constructor => FloatingPointTypes.FirstOf(
            [constructor.DeclaringType, .. constructor.GetParameters().Select(parameter => parameter.ParameterType)]),
        FieldInfo field => FloatingPointTypes.FirstOf([field.DeclaringType, field.FieldType]),
        Type type => FloatingPointTypes.Find(type),
        _ => null,
    };

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
