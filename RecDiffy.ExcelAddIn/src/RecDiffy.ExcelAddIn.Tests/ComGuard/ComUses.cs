using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace RecDiffy.ExcelAddIn.Tests.ComGuard;

internal static class ComUses
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const string OfficeInterop = "is Office interop, Excel's COM object model";
    private const string Dynamic = "is `dynamic`, which drives objects by late binding";
    private const string ComInterop = "is a COM interop assembly";
    private const string ComRuntime = "creates, wraps or releases COM objects";
    private const string ComExposure = "exposes our code to COM";

    private static readonly FrozenDictionary<string, string> TypeReasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ExcelDna.Integration.ExcelComAddIn"] = "is a COM add-in",
        ["ExcelDna.Integration.ExcelComAddInHelper"] = "loads COM add-ins",
        ["ExcelDna.Registration.VisualBasic.RangeParameterConversion"] = "converts arguments to Excel's COM Range",
        ["System.Runtime.InteropServices.ComWrappers"] = ComRuntime,
        ["System.Runtime.InteropServices.Marshalling.ComObject"] = ComRuntime,
        ["System.Runtime.InteropServices.Marshalling.StrategyBasedComWrappers"] = ComRuntime,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly (string Prefix, string Reason)[] NamespaceReasons =
    [
        ("Microsoft.Office", OfficeInterop),
        ("Microsoft.Vbe", "is VBA's COM interop"),
        ("NetOffice", "is NetOffice, a wrapper over Excel's COM object model"),
        ("Microsoft.CSharp.RuntimeBinder", Dynamic),
        ("ExcelDna.Integration.CustomUI", "is a ribbon, task pane or command bar, which Excel reaches through COM"),
        ("ExcelDna.Integration.Extensibility", "connects a COM add-in to Excel's COM Application object"),
        ("ExcelDna.Integration.Rtd", "is an RTD server, which is a COM server"),
        ("ExcelDna.ComInterop", "registers COM servers"),
        ("System.Runtime.InteropServices.ComTypes", "is a COM interface"),
    ];

    private static readonly FrozenDictionary<(string Type, string Member), string> MemberReasons =
        new (string Type, string[] Members, string Reason)[]
        {
            ("ExcelDna.Integration.ExcelDnaUtil", ["get_Application"], "is Excel's COM Application object"),
            ("ExcelDna.Integration.ExcelDnaUtil", ["get_WindowHandle"], "asks Excel's COM Application object for its window when it is cached; call XlCall.xlGetHwnd instead"),
            ("ExcelDna.Integration.XlCall", ["RTD"], "has Excel start an RTD server, which is a COM server"),
            ("ExcelDna.Integration.CustomUI.IRibbonControl", ["get_Context"], "is the Excel window the control belongs to, a COM object"),
            ("System.Type", ["GetTypeFromCLSID", "GetTypeFromProgID"], "finds a COM class to start"),
            ("System.Type", ["InvokeMember"], "drives an object by late binding, the way COM automation does"),
            ("Microsoft.VisualBasic.Interaction", ["CallByName", "CreateObject", "GetObject"], "starts or drives COM objects by late binding"),
            ("System.Runtime.InteropServices.Marshal",
            [
                "AddRef", "BindToMoniker", "ChangeWrapperHandleStrength", "CreateAggregatedObject", "CreateWrapperOfType",
                "FinalReleaseComObject", "GetComInterfaceForObject", "GetComObjectData", "GetEndComSlot", "GetIDispatchForObject",
                "GetIUnknownForObject", "GetNativeVariantForObject", "GetObjectForIUnknown", "GetObjectForNativeVariant",
                "GetObjectsForNativeVariants", "GetStartComSlot", "GetTypedObjectForIUnknown", "GetUniqueObjectForIUnknown",
                "IsComObject", "QueryInterface", "Release", "ReleaseComObject", "SetComObjectData",
            ], ComRuntime),
        }
        .SelectMany(group => group.Members.Select(member => KeyValuePair.Create((group.Type, member), group.Reason)))
        .ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> AttributeReasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["System.Runtime.InteropServices.ClassInterfaceAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.CoClassAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.ComDefaultInterfaceAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.ComRegisterFunctionAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.ComSourceInterfacesAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.ComUnregisterFunctionAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.InterfaceTypeAttribute"] = "declares a COM interface",
        ["System.Runtime.InteropServices.ProgIdAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.Marshalling.GeneratedComClassAttribute"] = ComExposure,
        ["System.Runtime.InteropServices.Marshalling.GeneratedComInterfaceAttribute"] = "declares a COM interface",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> AssemblyReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.CSharp"] = Dynamic,
        ["office"] = OfficeInterop,
        ["stdole"] = ComInterop,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Prefix, string Reason)[] AssemblyPrefixReasons =
    [
        ("Microsoft.Office.", OfficeInterop),
        ("Microsoft.Vbe.", ComInterop),
        ("NetOffice", ComInterop),
        ("Interop.", ComInterop),
    ];

    private static readonly FrozenSet<string> ComLibraries =
        new[] { "ole32", "oleaut32", "combase" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<OpCode>()
            .ToFrozenDictionary(code => code.Value);

    public static IReadOnlyList<ComUse> In(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var site = $"assembly {assembly.GetName().Name}";
        IEnumerable<ComUse> assemblyUses =
        [
            .. assembly.GetReferencedAssemblies().SelectMany(reference => ByAssembly(site, reference.Name)),
            .. assembly.GetCustomAttributesData().SelectMany(attribute => ByAttribute(site, attribute)),
        ];
        var typeUses = assembly.GetTypes()
            .Where(type => type.DeclaringType is null)
            .SelectMany(type => Within(type, type.FullName ?? type.Name));
        return Ordered(assemblyUses.Concat(typeUses));
    }

    public static IReadOnlyList<ComUse> Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Ordered(Within(type, type.FullName ?? type.Name));
    }

    public static string? WhyAssembly(string? name) => name switch
    {
        null => null,
        _ when AssemblyReasons.GetValueOrDefault(name) is { } reason => reason,
        _ => AssemblyPrefixReasons
            .Where(entry => name.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Reason)
            .FirstOrDefault(),
    };

    private static IReadOnlyList<ComUse> Ordered(IEnumerable<ComUse> uses) =>
    [
        .. uses
            .Distinct()
            .OrderBy(use => use.Site, StringComparer.Ordinal)
            .ThenBy(use => use.Api, StringComparer.Ordinal)
            .ThenBy(use => use.Member, StringComparer.Ordinal),
    ];

    private static IEnumerable<ComUse> Within(Type type, string site)
    {
        MethodBase[] methods = [.. type.GetMethods(Declared), .. type.GetConstructors(Declared)];
        Type[] signatureTypes =
        [
            .. type.BaseType is { } baseType ? [baseType] : Type.EmptyTypes,
            .. type.GetInterfaces().Except(type.BaseType?.GetInterfaces() ?? Type.EmptyTypes),
            .. type.GetFields(Declared).Select(field => field.FieldType),
            .. methods.SelectMany(SignatureTypes),
        ];
        return
        [
            .. Declares(type, site),
            .. type.GetCustomAttributesData().SelectMany(attribute => ByAttribute(site, attribute)),
            .. signatureTypes.SelectMany(signatureType => ByType(site, signatureType)),
            .. methods.SelectMany(method => InMethod(site, method)),
            .. type.GetNestedTypes(Declared).SelectMany(nested => Within(nested, site)),
        ];
    }

    private static IEnumerable<ComUse> Declares(Type type, string site) =>
        type.IsImport ? [new ComUse(site, type.FullName ?? type.Name, "", "declares a COM type with ComImport")] : [];

    private static IEnumerable<Type> SignatureTypes(MethodBase method) =>
    [
        .. method is MethodInfo info ? [info.ReturnType] : Type.EmptyTypes,
        .. method.GetParameters().Select(parameter => parameter.ParameterType),
    ];

    private static IEnumerable<ComUse> InMethod(string site, MethodBase method) =>
    [
        .. method.GetCustomAttributesData().SelectMany(attribute => ByAttribute(site, attribute)),
        .. ByPlatformInvoke(site, method),
        .. Referenced(method).SelectMany(member => ByMember(site, member)),
    ];

    private static IEnumerable<ComUse> ByPlatformInvoke(string site, MethodBase method) =>
        method.GetCustomAttribute<DllImportAttribute>() is { Value: var library } dllImport
        && ComLibraries.Contains(Path.GetFileNameWithoutExtension(library))
            ? [new ComUse(site, library, dllImport.EntryPoint ?? method.Name, "is COM's native API")]
            : [];

    private static IEnumerable<ComUse> ByAssembly(string site, string? name) =>
        WhyAssembly(name) is { } reason ? [new ComUse(site, name ?? "", "", reason)] : [];

    private static IEnumerable<ComUse> ByAttribute(string site, CustomAttributeData attribute) =>
        WhyAttribute(attribute) is { } reason ? [new ComUse(site, attribute.AttributeType.FullName ?? attribute.AttributeType.Name, "", reason)] : [];

    private static string? WhyAttribute(CustomAttributeData attribute) => attribute switch
    {
        { AttributeType.FullName: "System.Runtime.InteropServices.ComVisibleAttribute", ConstructorArguments: [{ Value: true }] } => ComExposure,
        { AttributeType.FullName: { } name } => AttributeReasons.GetValueOrDefault(name),
        _ => null,
    };

    private static IEnumerable<ComUse> ByMember(string site, MemberInfo member) => member switch
    {
        Type type => ByType(site, type),
        MethodInfo { IsGenericMethod: true } method =>
        [
            .. ByDeclaredMember(site, method),
            .. method.GetGenericArguments().SelectMany(argument => ByType(site, argument)),
        ],
        _ => ByDeclaredMember(site, member),
    };

    private static IEnumerable<ComUse> ByDeclaredMember(string site, MemberInfo member) => member.DeclaringType switch
    {
        null => [],
        var owner when MemberReasons.GetValueOrDefault((owner.FullName ?? owner.Name, member.Name)) is { } reason =>
        [
            .. ByType(site, owner),
            new ComUse(site, owner.FullName ?? owner.Name, member.Name, reason),
        ],
        var owner => ByType(site, owner),
    };

    private static IEnumerable<ComUse> ByType(string site, Type type) =>
        Named(type)
            .Select(named => (Named: named, Reason: WhyType(named)))
            .Where(entry => entry.Reason is not null)
            .Select(entry => new ComUse(site, entry.Named.FullName ?? entry.Named.Name, "", entry.Reason ?? ""));

    private static IEnumerable<Type> Named(Type type) => type switch
    {
        { IsGenericParameter: true } => [],
        { HasElementType: true } when type.GetElementType() is { } element => Named(element),
        { IsConstructedGenericType: true } => [type.GetGenericTypeDefinition(), .. type.GenericTypeArguments.SelectMany(Named)],
        _ => [type],
    };

    private static string? WhyType(Type type) => type switch
    {
        _ when TypeReasons.GetValueOrDefault(type.FullName ?? "") is { } reason => reason,
        { Namespace: { } name } when NamespaceReason(name) is { } reason => reason,
        { IsImport: true } => "is a COM type, declared with ComImport",
        _ => WhyAssembly(type.Assembly.GetName().Name),
    };

    private static string? NamespaceReason(string name) =>
        NamespaceReasons
            .Where(entry => name == entry.Prefix || name.StartsWith(entry.Prefix + ".", StringComparison.Ordinal))
            .Select(entry => entry.Reason)
            .FirstOrDefault();

    private static IEnumerable<MemberInfo> Referenced(MethodBase method)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null;
        return method.GetMethodBody()?.GetILAsByteArray() is { } il
            ? Tokens(il).Select(token => method.Module.ResolveMember(token, typeArguments, methodArguments)).OfType<MemberInfo>()
            : [];
    }

    private static IEnumerable<int> Tokens(byte[] il)
    {
        var offset = 0;
        while (offset < il.Length)
        {
            var (code, size) = il[offset] is 0xFE
                ? (OpCodesByValue[unchecked((short)(0xFE00 | il[offset + 1]))], 2)
                : (OpCodesByValue[il[offset]], 1);
            var operand = offset + size;
            if (code.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
                yield return BitConverter.ToInt32(il, operand);

            offset = operand + OperandSize(code.OperandType, il, operand);
        }
    }

    private static int OperandSize(OperandType type, byte[] il, int operand) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, operand)),
        _ => 4,
    };
}
