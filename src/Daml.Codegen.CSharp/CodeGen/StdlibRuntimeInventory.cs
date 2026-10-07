// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The disposition of every serializable type the Daml standard library packages declare:
/// hand-written into <c>Daml.Runtime</c>, generated into <c>Daml.Runtime</c> by
/// the internal stdlib runtime tooling, or excluded with a stated reason. A type absent from all
/// three has not been audited, which the inventory guard test over the vendored stable packages
/// reports.
/// </summary>
internal static class StdlibRuntimeInventory
{
    /// <summary>
    /// The types whose CLR identity predates the generated bindings. Consumers compile against
    /// them, so the generator skips them and their hand-written shapes stay as they are.
    /// </summary>
    public static IReadOnlySet<(string Module, string Name)> HandMapped { get; } = new HashSet<(string, string)>
    {
        ("DA.Date.Types", "DayOfWeek"),
        ("DA.Date.Types", "Month"),
        ("DA.Time.Types", "RelTime"),
        ("DA.Types", "Tuple2"),
        ("DA.Types", "Tuple3"),
        ("DA.Types", "Either"),
        ("DA.Set.Types", "Set"),
        ("DA.NonEmpty.Types", "NonEmpty"),
        ("DA.Map.Types", "Map"),
        ("DA.Internal.Map", "Map"),
    };

    /// <summary>
    /// The types generated into <c>Daml.Runtime.Stdlib</c>, each emitted by the ordinary record,
    /// variant and enum emitters from the vendored stable package definitions.
    /// </summary>
    public static IReadOnlySet<(string Module, string Name)> Generated { get; } = new HashSet<(string, string)>(
        new (string Module, string Name)[]
        {
            ("GHC.Types", "Ordering"),
            ("GHC.Tuple", "Unit"),
            ("DA.Monoid.Types", "All"),
            ("DA.Monoid.Types", "Any"),
            ("DA.Monoid.Types", "Product"),
            ("DA.Monoid.Types", "Sum"),
            ("DA.Semigroup.Types", "Max"),
            ("DA.Semigroup.Types", "Min"),
            ("DA.Internal.Down", "Down"),
            ("DA.Validation.Types", "Validation"),
            ("DA.Logic.Types", "Formula"),
            ("DA.Random.Types", "Minstd"),
            ("DA.Stack.Types", "SrcLoc"),
            ("DA.Exception.ArithmeticError", "ArithmeticError"),
            ("DA.Exception.AssertionFailed", "AssertionFailed"),
            ("DA.Exception.GeneralError", "GeneralError"),
            ("DA.Exception.PreconditionFailed", "PreconditionFailed"),
            ("DA.Internal.Template", "Archive"),
        }.Concat(Enumerable.Range(4, 17).Select(arity => ("DA.Types", $"Tuple{arity}"))));

    /// <summary>
    /// Serializable stdlib types that no <c>Daml.Runtime</c> binding covers, with the reason each
    /// is unsupported by this SDK for now. Empty: every serializable type in the SDK 3.5.2 stable
    /// packages is hand-mapped or generated.
    /// </summary>
    public static IReadOnlyDictionary<(string Module, string Name), string> Excluded { get; } =
        new Dictionary<(string, string), string>();
}
