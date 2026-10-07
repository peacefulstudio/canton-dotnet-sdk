// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// C# identifier sanitisation and casing shared by the emitter and its per-package
/// scan. Pure functions over a Daml name: escape invalid characters, avoid
/// leading-digit and keyword collisions, and PascalCase segment-delimited names.
/// </summary>
internal static class Identifiers
{
    /// <summary>The C# global namespace alias every fully qualified emitted name starts with.</summary>
    internal const string GlobalPrefix = "global::";

    /// <summary>
    /// Spells <paramref name="name"/> declared in <paramref name="dottedNamespace"/> as a
    /// <c>global::</c>-rooted fully qualified name, immune to a nearer declaration of the same
    /// spelling capturing it.
    /// </summary>
    internal static string GlobalQualified(string dottedNamespace, string name) =>
        $"{GlobalPrefix}{dottedNamespace}.{name}";

    /// <summary>
    /// True when <paramref name="dottedName"/> equals <paramref name="prefix"/> or begins with it
    /// at a segment boundary — <c>Acme.Ledger.Token</c> starts with <c>Acme.Ledger</c>, while
    /// <c>Acme.LedgerX</c> does not. The one test both the <c>-n</c> prefix elision and the
    /// namespace-ancestry walk rely on.
    /// </summary>
    internal static bool StartsWithSegments(string dottedName, string prefix) =>
        dottedName == prefix || dottedName.StartsWith(prefix + ".", StringComparison.Ordinal);

    /// <summary>
    /// Every namespace declaring <paramref name="dottedNamespace"/> implies, shortest first:
    /// <c>A.B.C</c> yields <c>A</c>, <c>A.B</c> and <c>A.B.C</c> itself.
    /// </summary>
    internal static IEnumerable<string> NamespaceWithAncestors(string dottedNamespace)
    {
        var segments = dottedNamespace.Split('.');
        for (var length = 1; length <= segments.Length; length++)
        {
            yield return string.Join('.', segments[..length]);
        }
    }

    /// <summary>
    /// Derives the C# namespace a Daml module's types are emitted into: the module name
    /// itself, each <c>.</c>-delimited segment leaf-sanitised (see <see cref="SanitizeBare"/>)
    /// and keyword-escaped, with the segment structure kept intact. When
    /// <see cref="CodeGenOptions.NamespacePrefix"/> is set and the module belongs to the main
    /// package, that value is prepended as a prefix — unless the module name already equals
    /// the prefix or begins with it at a segment boundary, in which case the module name is
    /// returned unchanged so <c>Acme.Ledger</c> + <c>Acme.Ledger.Token</c> is
    /// <c>Acme.Ledger.Token</c>, not <c>Acme.Ledger.Acme.Ledger.Token</c>. The elision is a
    /// whole-prefix test on segment boundaries, never a longest-common-suffix match: suffix
    /// matching would send <c>Ledger.Token</c> and <c>Token</c> to the same namespace.
    /// Dependency packages never receive the prefix, so a dependency generated locally is
    /// interchangeable with the same package's published bindings, which are emitted with no
    /// override.
    /// </summary>
    public static string ModuleNamespace(string moduleName, bool isMainPackage, CodeGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(moduleName);
        ArgumentNullException.ThrowIfNull(options);

        var sanitisedModule = string.Join('.', moduleName.Split('.').Select(segment => EscapeKeyword(SanitizeBare(segment))));
        var prefix = options.NamespacePrefix;
        if (prefix is null || !isMainPackage)
        {
            return sanitisedModule;
        }
        return StartsWithSegments(sanitisedModule, prefix)
            ? sanitisedModule
            : prefix + "." + sanitisedModule;
    }

    /// <summary>
    /// Maps a Daml name to a legal C# identifier. A synthetic variant-payload data type
    /// carries a compound <c>Type.Constructor</c> name (e.g. <c>Outcome.Win</c>) whose
    /// <c>.</c> is a structural separator, not identifier content, so each
    /// <c>.</c>-delimited segment is leaf-sanitised (see <see cref="SanitizeBare"/>) and
    /// the segments are rejoined with <c>_</c> to a flat C# type name
    /// (<c>Outcome.Win</c> → <c>Outcome_Win</c>). C# keywords are escaped with <c>@</c>
    /// last. A leaf name never contains a <c>.</c> (damlc mangles it), so a single-segment
    /// name passes through <see cref="SanitizeBare"/> unchanged.
    /// </summary>
    public static string Sanitize(string name) =>
        EscapeKeyword(string.Join('_', name.Split('.').Select(SanitizeBare)));

    /// <summary>
    /// Injectively maps a Daml LF name to a C# identifier body, leaving keyword
    /// collisions unescaped. Demangles damlc's mangling (<c>$$</c> → <c>$</c>,
    /// <c>$uXXXX</c> → the UTF-16 code unit) and then escapes every character C#
    /// forbids as <c>_uXXXX</c> (lowercase hex), doubling a literal <c>_</c> to
    /// <c>__</c> only where it would otherwise read back as an escape, and escaping a
    /// leading digit rather than prefixing it — so distinct Daml names never share a
    /// C# identifier. Callers that further transform the result — recasing or
    /// prefixing it — must compose this with <see cref="EscapeKeyword"/> applied last,
    /// because a transform both introduces collisions an earlier escape cannot see
    /// (Daml <c>Operator</c> recases to the keyword <c>operator</c>) and invalidates
    /// ones an earlier escape wrongly applied (a type variable <c>event</c> escaped to
    /// <c>@event</c> cannot then be prefixed: <c>T@event</c> parses as two identifiers,
    /// not one).
    /// </summary>
    /// <remarks>
    /// The demangling step is injective only over the image of damlc's mangler: a name
    /// carrying a raw unpaired <c>$</c> would be mis-decoded (both <c>$</c> and
    /// <c>$$</c> demangle to <c>$</c>). The emitter only ever sees damlc output, where
    /// every <c>$</c> is paired or introduces a well-formed <c>$uXXXX</c> escape, so the
    /// precondition holds.
    /// </remarks>
    internal static string SanitizeBare(string name) => EscapeToIdentifier(Demangle(name));

    private static string Demangle(string mangled)
    {
        var demangled = new StringBuilder(mangled.Length);
        var i = 0;
        while (i < mangled.Length)
        {
            if (mangled[i] == '$' && i + 1 < mangled.Length && mangled[i + 1] == '$')
            {
                demangled.Append('$');
                i += 2;
            }
            else if (mangled[i] == '$' && i + 5 < mangled.Length && mangled[i + 1] == 'u'
                && IsLowerHexDigit(mangled[i + 2]) && IsLowerHexDigit(mangled[i + 3])
                && IsLowerHexDigit(mangled[i + 4]) && IsLowerHexDigit(mangled[i + 5]))
            {
                demangled.Append((char)Convert.ToInt32(mangled.Substring(i + 2, 4), 16));
                i += 6;
            }
            else
            {
                demangled.Append(mangled[i]);
                i++;
            }
        }
        return demangled.ToString();
    }

    private static string EscapeToIdentifier(string demangled)
    {
        var fragments = new List<string>(demangled.Length);
        var tailPrefix = string.Empty;
        for (var i = demangled.Length - 1; i >= 0; i--)
        {
            var c = demangled[i];
            string fragment;
            if (c == '_')
            {
                fragment = TailWouldExtendUnderscoreEscape(tailPrefix) ? "__" : "_";
            }
            else if (char.IsAsciiLetterOrDigit(c))
            {
                fragment = c.ToString();
            }
            else
            {
                fragment = HexEscape(c);
            }

            fragments.Add(fragment);
            var combined = fragment + tailPrefix;
            tailPrefix = combined.Length > UnderscoreEscapeLookahead ? combined[..UnderscoreEscapeLookahead] : combined;
        }

        fragments.Reverse();
        var result = string.Concat(fragments);
        return result.Length > 0 && char.IsAsciiDigit(result[0])
            ? HexEscape(result[0]) + result[1..]
            : result;
    }

    private const int UnderscoreEscapeLookahead = 5;

    private static bool TailWouldExtendUnderscoreEscape(string encodedTail) =>
        encodedTail.StartsWith('_') || BeginsWithHexEscapeBody(encodedTail);

    private static bool BeginsWithHexEscapeBody(string encodedTail) =>
        encodedTail.Length >= 5
        && encodedTail[0] == 'u'
        && IsLowerHexDigit(encodedTail[1])
        && IsLowerHexDigit(encodedTail[2])
        && IsLowerHexDigit(encodedTail[3])
        && IsLowerHexDigit(encodedTail[4]);

    private static string HexEscape(char c) => "_u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);

    private static bool IsLowerHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';

    /// <summary>
    /// Prefixes <c>@</c> when <paramref name="identifier"/> collides with a C# keyword,
    /// making it legal as a declaration. Apply last, once every casing and prefixing
    /// transform has run — see <see cref="SanitizeBare"/>.
    /// </summary>
    internal static string EscapeKeyword(string identifier) =>
        CSharpKeywords.Contains(identifier) ? "@" + identifier : identifier;

    /// <summary>
    /// Strips the keyword-escaping <c>@</c> for an XML doc <c>name=</c> attribute, which
    /// binds to a declared symbol by its bare name and rejects the escape (CS1572 on the
    /// tag, CS1573 on the now-undocumented parameter). Exact because
    /// <see cref="SanitizeBare"/> rewrites every <c>@</c> carried by the Daml name to
    /// <c>_</c>, so the only <c>@</c> that can reach an emitted identifier is the single
    /// leading one <see cref="EscapeKeyword"/> prepends.
    /// </summary>
    internal static string DocCommentName(string identifier) => identifier.TrimStart('@');

    /// <summary>
    /// PascalCases a name across <c>_</c>, <c>-</c> and <c>.</c> delimiters,
    /// prefixing a leading-digit result with <c>_</c>.
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder();
        var capitalizeNext = true;

        foreach (var c in name)
        {
            if (c is '_' or '-' or '.')
            {
                capitalizeNext = true;
            }
            else if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();

        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            return "_" + result;
        }

        return result;
    }

    /// <summary>
    /// Derives the C# member identifier for a Daml field: leaf-sanitised
    /// (<see cref="SanitizeBare"/>), PascalCased, disambiguated with a trailing
    /// <c>_</c> when the result would equal <paramref name="enclosingTypeName"/>
    /// (illegal in C#: CS0542 member names cannot be the same as their enclosing
    /// type), and keyword-escaped last. The order is load-bearing: escaping first
    /// leaves <see cref="ToPascalCase"/> spending its capitalisation on the
    /// <c>@</c> rather than on the letter behind it, so Daml <c>lock</c> would
    /// emit a lowercase <c>@lock</c> among PascalCased siblings. Escaping last, the
    /// escape is in fact unreachable — <see cref="ToPascalCase"/> never returns a
    /// lowercase-initial name and every entry of the C# keyword set is lowercase —
    /// but it is composed anyway so the identifier stays legal if the casing step
    /// changes. A leaf Daml name never contains a <c>.</c>, so
    /// <see cref="SanitizeBare"/> covers everything <see cref="Sanitize"/> would.
    /// The Daml wire name is unaffected — only the emitted C# identifier changes.
    /// </summary>
    internal static string MemberName(string damlFieldName, string enclosingTypeName) =>
        EscapeKeyword(Disambiguate(ToPascalCase(SanitizeBare(damlFieldName)), enclosingTypeName));

    /// <summary>
    /// As <see cref="MemberName(string, string)"/>, and a name that equals one of
    /// <paramref name="reservedMemberNames"/> (a member the enclosing generated type declares
    /// itself) gains one trailing <c>_</c>, and a further one when the suffixed name is the
    /// enclosing type's own (a type renamed to <c>ToRecord_</c> beside a field that PascalCases to
    /// <c>ToRecord</c>). <see cref="ToPascalCase"/> drops underscores, so the suffixed name cannot
    /// equal another field's.
    /// </summary>
    internal static string MemberName(string damlFieldName, string enclosingTypeName, IReadOnlySet<string> reservedMemberNames)
    {
        var name = MemberName(damlFieldName, enclosingTypeName);
        return reservedMemberNames.Contains(name) ? EscapeKeyword(Disambiguate(name + "_", enclosingTypeName)) : name;
    }

    /// <summary>
    /// Builds the C# marker-interface name for a Daml interface: the sanitised
    /// interface name prefixed with <c>I</c> (e.g. Daml <c>Holding</c> →
    /// <c>IHolding</c>), appending a trailing <c>_</c> until the result is absent
    /// from <paramref name="reservedTypeNames"/> — the sanitised names of every
    /// top-level Daml declaration in the declaring package (template, record, enum,
    /// or variant), whichever module declares it, since any of them can legally
    /// sanitise to an interface marker (e.g. record <c>IFactory</c> alongside
    /// interface <c>Factory</c>) and two such declarations in one namespace would be
    /// CS0101. Reserving across the whole package rather than the marker's own
    /// namespace over-approximates that collision, but keeps the assignment
    /// independent of how modules map to namespaces. The name is built from
    /// <see cref="SanitizeBare"/> with <see cref="EscapeKeyword"/> applied last:
    /// escaping first would emit <c>I@event</c>, which parses as two identifiers
    /// rather than one. The escape is unreachable — the <c>I</c> prefix leaves an
    /// uppercase-initial name and every C# keyword is lowercase — and is composed
    /// only so the prefix is not the sole guarantee. Shared by the interface emitter
    /// and the type resolver so a reference to an interface names the same marker on
    /// the field-type path as on the choice-exercise path — callers must pass the
    /// declaring package's widened top-level type-name set
    /// (<see cref="PackageNameTable.ReservedTopLevelTypeNames"/>) for every reference
    /// to a given interface.
    /// </summary>
    /// <param name="interfaceName">The Daml interface's simple name, before sanitisation.</param>
    /// <param name="reservedTypeNames">
    /// The declaring package's sanitised top-level type names — every template plus
    /// every record/enum/variant, excluding interface-placeholder and choice-argument
    /// records (see <see cref="PackageNameTable.ReservedTopLevelTypeNames"/>) — that the
    /// returned marker must not collide with.
    /// </param>
    internal static string InterfaceMarkerName(string interfaceName, IReadOnlySet<string> reservedTypeNames)
    {
        var marker = "I" + SanitizeBare(interfaceName);
        while (reservedTypeNames.Contains(marker))
        {
            marker += "_";
        }
        return EscapeKeyword(marker);
    }

    /// <summary>
    /// Appends a trailing <c>_</c> when <paramref name="identifier"/> equals
    /// <paramref name="enclosingTypeName"/>, which is illegal in C# (CS0542: member
    /// names cannot be the same as their enclosing type).
    /// </summary>
    internal static string Disambiguate(string identifier, string enclosingTypeName) =>
        identifier == enclosingTypeName ? identifier + "_" : identifier;

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];
}
