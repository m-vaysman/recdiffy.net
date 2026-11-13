---
name: invariant-formatting
description: Format numbers, dates and other IFormattable values culture-invariantly in RecDiffy with the ToInvariant() extension. Use whenever you write or review an interpolated string, Excel formula, cell reference, key, or any text a machine reads back, or when MA0076 or MA0185 fires. Not for text meant only for a person to read in their own culture.
---

# Invariant formatting with `ToInvariant()`

An interpolated hole such as `$"{row}"` formats through the current culture. On de-DE a decimal prints `0,5`, which breaks an Excel formula, and dates come out in a different order. RecDiffy fixes the culture **one value at a time** with `RecDiffy.InvariantFormatting` (`RecDiffy.NET/src/RecDiffy/InvariantFormatting.cs`).

## The rule

Apply `ToInvariant()` to each non-string value inside the interpolation, not to the whole string.

```csharp
// Yes
$"IF(H{row.ToInvariant()}=0,0,ABS(G{row.ToInvariant()})/H{row.ToInvariant()})"
$"{Column(column)}{row.ToInvariant()}"
$"{amount.ToInvariant("F2")} USD"

// No: whole-string wrappers
FormattableString.Invariant($"H{row}")
string.Create(CultureInfo.InvariantCulture, $"H{row}")

// No: leaving a number to the current culture
$"H{row}"
```

The lead rejected whole-string wrappers. The per-value call keeps the rest of the string as written and shows at each hole exactly which value is being made invariant.

## The two overloads

- `value.ToInvariant()` formats with the type's default format.
- `value.ToInvariant(format)` takes a standard or custom format string, such as `"F2"`, `"yyyy-MM-dd"` or `"0.####"`. A `null` format means the default.

Both require `T : IFormattable`.

## What doesn't need it, or won't compile

- **Strings.** A `string` hole is already culture-free, and `string` isn't `IFormattable`, so `name.ToInvariant()` doesn't compile. Leave it as `{name}`.
- **Nullable value types.** `int?` isn't `IFormattable`, so `count.ToInvariant()` doesn't compile when `count` is `int?`. Decide what null means first, for example `count?.ToInvariant() ?? ""` or `count.GetValueOrDefault().ToInvariant()`, and keep that choice visible.
- **Null references.** Calling it on a null `IFormattable` reference throws `NullReferenceException`. Guard first.
- **`char`, `bool` and enums.** `char` and `bool` aren't `IFormattable`, but they don't vary by culture, so interpolate them directly. Enums are `IFormattable`; use `ToInvariant()` only if MA0076 asks for it.

## Analyzers that enforce this

- **MA0076** (implicit culture-sensitive `ToString` in an interpolated string) is `error` for production code in `RecDiffy.NET/.editorconfig`, and `suggestion` in the three test projects' `.editorconfig` files. Fix it with `ToInvariant()`. Don't suppress it.
- **MA0185** (simplify `string.Create` when every parameter is culture-invariant) fires when a `string.Create(CultureInfo.InvariantCulture, ...)` has only string holes. Use a plain interpolated string.
- SonarQube imports both. Its quality gate fails on **any** new issue, even an info-level one.

## Public by necessity

`InvariantFormatting` is public because the samples and package smoke tests compile against the packed NuGet packages, where an internal helper isn't visible. The solution build doesn't catch that case; the `Packages` workflow does. If you touch anything the samples consume, run the package smoke scripts before you push.

## When text is meant for a person

Text shown only to a person in their own culture, such as a UI label, can use the current culture deliberately. Say so in a comment, pass `CultureInfo.CurrentCulture` explicitly, and don't use `ToInvariant()` there.
