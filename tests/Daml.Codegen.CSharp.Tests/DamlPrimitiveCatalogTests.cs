// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using PbType = Daml.Codegen.Intermediate.Type;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Completeness reflection tests over <see cref="DamlPrimitiveCatalog"/>: the catalog is
/// the single place the "adding a Daml primitive" rules live as code, so these tests pin
/// that it stays in exact bijection with the proto
/// <see cref="BuiltinType"/> enum (all 23 defined values,
/// <c>BUILTIN_TYPE_UNSPECIFIED</c> = 0 through <c>BUILTIN_TYPE_FAILURE_CATEGORY</c> = 22,
/// proto/intermediate_dar.proto:174-198) and that every row is well-formed and backed by
/// the runtime, the emitter, and the value model wherever its disposition claims backing.
/// </summary>
public sealed class DamlPrimitiveCatalogTests
{
    [Fact]
    public void DamlPrimitiveCatalog_rows_form_a_bijection_with_the_proto_builtin_type_values()
    {
        var protoValues = System.Enum.GetValues<BuiltinType>();

        protoValues.Should().HaveCount(23,
            "the proto schema defines 23 BuiltinType values: BUILTIN_TYPE_UNSPECIFIED = 0 through "
            + "BUILTIN_TYPE_FAILURE_CATEGORY = 22");

        DamlPrimitiveCatalog.Rows.Should().HaveCount(protoValues.Length,
            "the catalog must carry exactly one row per defined proto BuiltinType value");

        foreach (var value in protoValues)
        {
            DamlPrimitiveCatalog.Rows.Should().ContainSingle(row => row.Builtin == value,
                "every defined proto BuiltinType value must have exactly one catalog row");
        }

        DamlPrimitiveCatalog.Rows.Should().OnlyContain(row => System.Enum.IsDefined(row.Builtin),
            "no catalog row may be keyed to a value the proto does not define");
    }

    [Fact]
    public void DamlPrimitiveCatalog_every_row_is_well_formed()
    {
        foreach (var row in DamlPrimitiveCatalog.Rows)
        {
            System.Enum.IsDefined(row.Disposition).Should().BeTrue(
                $"the row for {row.Builtin} must carry one of the three named dispositions");
            row.Rationale.Should().NotBeNullOrWhiteSpace(
                $"the row for {row.Builtin} must record why it carries its disposition");
            row.Arity.Should().BeGreaterThanOrEqualTo(0,
                $"the row for {row.Builtin} must pin a non-negative Daml-LF arity");

            switch (row.Disposition)
            {
                case DamlPrimitiveDisposition.SupportedValue:
                case DamlPrimitiveDisposition.SignatureOnly:
                    row.Primitive.Should().NotBeNull(
                        because: $"a {row.Disposition} row carries a real DamlPrimitive identity — " +
                                 $"{row.Builtin} must not be silently erased to Unit");
                    break;
                case DamlPrimitiveDisposition.Unsupported:
                    row.Primitive.Should().BeNull(
                        because: $"an Unsupported row carries no model identity — encountering {row.Builtin} " +
                                 "throws, so there is nothing to map to");
                    break;
            }
        }
    }

    /// <summary>
    /// The number of <see cref="DamlPrimitiveDisposition.SupportedValue"/> rows the catalog
    /// must carry. This is a parameter of the policy test, not a derived fact: it interlocks
    /// with the FAILURE_CATEGORY characterization (VAL-CAT-006, M2) — the characterization
    /// landed SignatureOnly, so the count stays at the 13 value types that were already
    /// mapped at the mission base. Had it landed SupportedValue, this parameter and the
    /// expectation table below would both be 14, and the extra runtime/emitter/value backing
    /// that implies would have been an explicit scope escalation, not a silent addition.
    /// </summary>
    private const int ExpectedSupportedValueRowCount = 13;

    public static TheoryData<BuiltinType, DamlPrimitiveDisposition> ApprovedPolicyRows() => new()
    {
        // The 13 serializable value types that were mapped at the mission base.
        { BuiltinType.Unit, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Bool, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Int64, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Text, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Numeric, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Party, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Date, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Timestamp, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.List, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.Optional, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.TextMap, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.GenMap, DamlPrimitiveDisposition.SupportedValue },
        { BuiltinType.ContractId, DamlPrimitiveDisposition.SupportedValue },

        // The six structural type-formers: legal in signature positions, real identity,
        // never silently erased to Unit. FAILURE_CATEGORY per its characterization
        // (FailureCategoryCharacterizationTests).
        { BuiltinType.Any, DamlPrimitiveDisposition.SignatureOnly },
        { BuiltinType.TypeRep, DamlPrimitiveDisposition.SignatureOnly },
        { BuiltinType.AnyException, DamlPrimitiveDisposition.SignatureOnly },
        { BuiltinType.Update, DamlPrimitiveDisposition.SignatureOnly },
        { BuiltinType.Arrow, DamlPrimitiveDisposition.SignatureOnly },
        { BuiltinType.FailureCategory, DamlPrimitiveDisposition.SignatureOnly },

        // Value-level types with no correct serialization: encountering them throws.
        { BuiltinType.RoundingMode, DamlPrimitiveDisposition.Unsupported },
        { BuiltinType.Bignumeric, DamlPrimitiveDisposition.Unsupported },

        // Values that cannot legitimately appear: the proto zero-value, and the
        // reserved-and-removed Daml-LF 2.x Scenario (reserved 1004).
        { BuiltinType.Unspecified, DamlPrimitiveDisposition.Unsupported },
        { BuiltinType.Scenario, DamlPrimitiveDisposition.Unsupported },
    };

    [Theory]
    [MemberData(nameof(ApprovedPolicyRows))]
    public void DamlPrimitiveCatalog_dispositions_match_the_approved_policy_table(
        BuiltinType builtin,
        DamlPrimitiveDisposition expectedDisposition)
    {
        var row = DamlPrimitiveCatalog.Get(builtin);

        row.Disposition.Should().Be(expectedDisposition,
            "the disposition of every proto BuiltinType value is fixed by the user-approved catalog policy");
    }

    [Fact]
    public void DamlPrimitiveCatalog_supported_value_row_count_matches_the_failure_category_characterization_outcome()
    {
        var supportedCount = DamlPrimitiveCatalog.Rows.Count(
            row => row.Disposition == DamlPrimitiveDisposition.SupportedValue);

        supportedCount.Should().Be(ExpectedSupportedValueRowCount,
            "the SupportedValue count interlocks with the FAILURE_CATEGORY characterization outcome " +
            "(SignatureOnly), so the catalog gains no value-type row beyond the 13 mapped at the mission base");
    }

    [Fact]
    public void DamlPrimitiveCatalog_failure_category_row_cites_the_characterization_evidence()
    {
        var row = DamlPrimitiveCatalog.Get(BuiltinType.FailureCategory);

        row.Disposition.Should().Be(DamlPrimitiveDisposition.SignatureOnly);
        row.Rationale.Should().Contain("splice",
            "the row must cite the fixture evidence the characterization recorded")
            .And.Contain("Serializable",
                "the row must confront the Canton 3.5 `instance Serializable FailureCategory` documentation " +
                "against the observed pool-only occurrences");
    }

    /// <summary>
    /// The Daml-LF arity of every proto <see cref="BuiltinType"/> value — all 23 pinned,
    /// including the deliberate edges: NUMERIC's arity 1 is the Nat pun (the scale arrives
    /// as a <see cref="DamlTypeVar"/>), SCENARIO keeps its historical arity even though it is
    /// reserved in Daml-LF 2.x and can never appear, and UNSPECIFIED is pinned at 0 because
    /// an unset proto field carries no type arguments.
    /// </summary>
    public static TheoryData<BuiltinType, int> DamlLfArities() => new()
    {
        { BuiltinType.Unspecified, 0 },
        { BuiltinType.Unit, 0 },
        { BuiltinType.Bool, 0 },
        { BuiltinType.Int64, 0 },
        { BuiltinType.Text, 0 },
        { BuiltinType.Numeric, 1 },
        { BuiltinType.Party, 0 },
        { BuiltinType.Date, 0 },
        { BuiltinType.Timestamp, 0 },
        { BuiltinType.List, 1 },
        { BuiltinType.Optional, 1 },
        { BuiltinType.TextMap, 1 },
        { BuiltinType.GenMap, 2 },
        { BuiltinType.ContractId, 1 },
        { BuiltinType.Any, 0 },
        { BuiltinType.TypeRep, 0 },
        { BuiltinType.RoundingMode, 0 },
        { BuiltinType.Bignumeric, 0 },
        { BuiltinType.AnyException, 0 },
        { BuiltinType.Update, 1 },
        { BuiltinType.Scenario, 1 },
        { BuiltinType.Arrow, 2 },
        { BuiltinType.FailureCategory, 0 },
    };

    [Theory]
    [MemberData(nameof(DamlLfArities))]
    public void DamlPrimitiveCatalog_arities_pin_the_daml_lf_builtin_arities(BuiltinType builtin, int expectedArity)
    {
        DamlPrimitiveCatalog.Get(builtin).Arity.Should().Be(expectedArity,
            "the catalog pins the Daml-LF arity of every proto BuiltinType value, including the " +
            "NUMERIC Nat pun (arity 1) and the reserved SCENARIO (arity 1)");
    }

    /// <summary>
    /// The documented backing every
    /// <see cref="DamlPrimitiveDisposition.SupportedValue"/> row must keep: the C# type
    /// <c>DamlTypeMapper.MapType</c> renders (probed shape-aware — arity-0 rows bare,
    /// arity-1/2 rows APPLIED, since the bare form of a type constructor throws by design),
    /// the runtime value type name the to/from-<c>DamlValue</c> conversions reference, and
    /// which conversion direction carries that reference (Party and ContractId serialize
    /// through instance methods, so only the deserializer spells the runtime type).
    /// </summary>
    public sealed record SupportedValueBacking(
        string ExpectedMappedTypeContains,
        string? ExpectedToValueContains,
        string? ExpectedFromValueContains);

    private static readonly IReadOnlyDictionary<DamlPrimitive, SupportedValueBacking> SupportedValueBackings =
        new Dictionary<DamlPrimitive, SupportedValueBacking>
        {
            [DamlPrimitive.Unit] = new("DamlUnit", "DamlUnit", "DamlUnit"),
            [DamlPrimitive.Bool] = new("bool", "DamlBool", "DamlBool"),
            [DamlPrimitive.Int64] = new("long", "DamlInt64", "DamlInt64"),
            [DamlPrimitive.Text] = new("string", "DamlText", "DamlText"),
            [DamlPrimitive.Numeric] = new("decimal", "DamlNumeric", "DamlNumeric"),
            [DamlPrimitive.Party] = new("Party", null, "DamlParty"),
            [DamlPrimitive.Date] = new("DateOnly", "DamlDate", "DamlDate"),
            [DamlPrimitive.Timestamp] = new("DateTimeOffset", "DamlTimestamp", "DamlTimestamp"),
            [DamlPrimitive.List] = new("IReadOnlyList<", "DamlList", "DamlList"),
            [DamlPrimitive.Optional] = new("string?", "DamlOptional", null),
            [DamlPrimitive.TextMap] = new("IReadOnlyDictionary<string,", "DamlTextMap", "DamlTextMap"),
            [DamlPrimitive.GenMap] = new("IReadOnlyDictionary<", "DamlGenMap", "DamlGenMap"),
            [DamlPrimitive.ContractId] = new("ContractId<", null, "DamlContractId"),
        };

    public static TheoryData<DamlPrimitive, int, SupportedValueBacking> SupportedValueBackingCases()
    {
        var cases = new TheoryData<DamlPrimitive, int, SupportedValueBacking>();
        foreach (var row in DamlPrimitiveCatalog.Rows.Where(
                     row => row.Disposition == DamlPrimitiveDisposition.SupportedValue))
        {
            cases.Add(row.Primitive!.Value, row.Arity, SupportedValueBackings[row.Primitive.Value]);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(SupportedValueBackingCases))]
    public void DamlPrimitiveCatalog_supported_value_rows_have_emitter_runtime_and_value_backing(
        DamlPrimitive primitive,
        int arity,
        SupportedValueBacking backing)
    {
        var mapper = NewMapper();
        var probe = BuildProbeType(primitive, arity);

        var mapped = mapper.MapType(probe);
        mapped.Should().NotBe("object",
            "a SupportedValue builtin must map to its documented C# type, never fall through " +
            $"to the object default at DamlTypeMapper's catch-all — {primitive}");
        mapped.Should().Contain(backing.ExpectedMappedTypeContains,
            $"{primitive} renders as its documented C# type in data positions");

        if (backing.ExpectedToValueContains is { } toValueExpectation)
        {
            mapper.ToValue(probe, "field").Should().Contain(toValueExpectation,
                $"{primitive}'s serialize conversion must reference its runtime value type");
        }

        if (backing.ExpectedFromValueContains is { } fromValueExpectation)
        {
            mapper.FromValue(probe, "value").Should().Contain(fromValueExpectation,
                $"{primitive}'s deserialize conversion must reference its runtime value type");
        }

        var runtimeValueTypeName = backing.ExpectedToValueContains ?? backing.ExpectedFromValueContains;
        runtimeValueTypeName.Should().NotBeNull(
            $"at least one conversion direction must reference {primitive}'s runtime value type");

        typeof(RuntimeTypeNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => (string?)field.GetRawConstantValue())
            .Should().Contain(runtimeValueTypeName,
                $"every SupportedValue primitive must have a RuntimeTypeNames entry — {primitive}");

        typeof(Daml.Runtime.Data.DamlValue).Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(Daml.Runtime.Data.DamlValue)))
            .Select(type => type.Name)
            .Should().Contain(runtimeValueTypeName!,
                $"every SupportedValue primitive must have a concrete DamlValue subtype in Daml.Runtime — {primitive}");
    }

    [Fact]
    public void DamlPrimitiveCatalog_supported_value_backing_expectations_cover_every_supported_value_row()
    {
        var supportedPrimitives = DamlPrimitiveCatalog.Rows
            .Where(row => row.Disposition == DamlPrimitiveDisposition.SupportedValue)
            .Select(row => row.Primitive!.Value)
            .ToList();

        supportedPrimitives.Should().HaveCount(ExpectedSupportedValueRowCount);

        SupportedValueBackings.Keys.ToHashSet().Should().BeEquivalentTo(supportedPrimitives,
            "the backing expectation table must cover exactly the catalog's SupportedValue rows, " +
            "so a newly promoted primitive cannot silently skip the backing probes");
    }

    [Fact]
    public void DamlPrimitiveCatalog_shipped_intermediate_binpbs_carry_no_builtin_whose_disposition_is_unsupported()
    {
        var shippedBinpbs = Directory.EnumerateFiles(
                AppContext.BaseDirectory, "intermediate.binpb", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();

        shippedBinpbs.Should().HaveCount(13,
            "the repo ships exactly one intermediate.binpb per snapshot family (12) plus the Quickstart " +
            "sample; the conformance corpus intermediates are SHA-pinned transients produced by " +
            "scripts/codegen-determinism.sh, not shipped files");

        var violations = new List<string>();
        foreach (var path in shippedBinpbs)
        {
            var fixtureName = Path.GetFileName(Path.GetDirectoryName(path));
            var proto = IntermediateDar.Parser.ParseFrom(File.ReadAllBytes(path));
            foreach (var (builtin, position) in EnumerateTypePositions(proto))
            {
                if (DamlPrimitiveCatalog.Get(builtin).Disposition == DamlPrimitiveDisposition.Unsupported)
                {
                    violations.Add($"{fixtureName}: {position} carries {builtin}");
                }
            }
        }

        violations.Should().BeEmpty(
            "no Type anywhere in a shipped corpus/snapshot intermediate may carry a builtin whose " +
            "catalog disposition is Unsupported — a hit would mean the fixture can no longer be read " +
            "under the catalog, a scope escalation rather than a test fix");
    }

    private static IEnumerable<(BuiltinType Builtin, string Position)> EnumerateTypePositions(IntermediateDar proto)
    {
        foreach (var package in new[] { proto.Main }.Where(p => p is not null).Concat(proto.Dependencies))
        {
            var packageName = package.PackageName;
            foreach (var module in package.Modules)
            {
                var moduleName = string.Join(".", module.NameSegments);
                foreach (var dataType in module.DataTypes)
                {
                    if (dataType.Record is not null)
                    {
                        foreach (var field in dataType.Record.Fields)
                        {
                            foreach (var builtin in BuiltinsIn(field.Type))
                            {
                                yield return (builtin, $"{packageName}:{moduleName}:{dataType.Name}.{field.Name}");
                            }
                        }
                    }
                    if (dataType.Variant is not null)
                    {
                        foreach (var constructor in dataType.Variant.Constructors)
                        {
                            foreach (var builtin in BuiltinsIn(constructor.Type))
                            {
                                yield return (builtin, $"{packageName}:{moduleName}:{dataType.Name}.{constructor.Name}");
                            }
                        }
                    }
                }
                foreach (var template in module.Templates)
                {
                    foreach (var builtin in BuiltinsIn(template.KeyType))
                    {
                        yield return (builtin, $"{packageName}:{moduleName}:{template.Name}:key");
                    }
                    foreach (var choice in template.Choices)
                    {
                        foreach (var builtin in BuiltinsIn(choice.ArgumentType))
                        {
                            yield return (builtin, $"{packageName}:{moduleName}:{template.Name}:{choice.Name}:arg");
                        }
                        foreach (var builtin in BuiltinsIn(choice.ReturnType))
                        {
                            yield return (builtin, $"{packageName}:{moduleName}:{template.Name}:{choice.Name}:ret");
                        }
                    }
                }
                foreach (var iface in module.Interfaces)
                {
                    foreach (var builtin in BuiltinsIn(iface.ViewType))
                    {
                        yield return (builtin, $"{packageName}:{moduleName}:{iface.Name}:view");
                    }
                    foreach (var method in iface.Methods)
                    {
                        foreach (var builtin in BuiltinsIn(method.ReturnType))
                        {
                            yield return (builtin, $"{packageName}:{moduleName}:{iface.Name}:method:{method.Name}");
                        }
                    }
                    foreach (var choice in iface.Choices)
                    {
                        foreach (var builtin in BuiltinsIn(choice.ArgumentType))
                        {
                            yield return (builtin, $"{packageName}:{moduleName}:{iface.Name}:{choice.Name}:arg");
                        }
                        foreach (var builtin in BuiltinsIn(choice.ReturnType))
                        {
                            yield return (builtin, $"{packageName}:{moduleName}:{iface.Name}:{choice.Name}:ret");
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<BuiltinType> BuiltinsIn(PbType? type)
    {
        if (type is null)
        {
            yield break;
        }
        switch (type.SortCase)
        {
            case PbType.SortOneofCase.Builtin:
                yield return type.Builtin;
                break;
            case PbType.SortOneofCase.TypeApp:
                foreach (var builtin in BuiltinsIn(type.TypeApp.Function))
                {
                    yield return builtin;
                }
                foreach (var argument in type.TypeApp.Arguments)
                {
                    foreach (var builtin in BuiltinsIn(argument))
                    {
                        yield return builtin;
                    }
                }
                break;
        }
    }

    private static DamlType BuildProbeType(DamlPrimitive primitive, int arity)
    {
        var bare = new DamlPrimitiveType(primitive);
        return arity switch
        {
            // Arity-0 rows are probed bare: they are complete value types on their own.
            0 => bare,
            // Arity-1/2 rows are probed APPLIED: the bare form of a type constructor throws
            // by design (DamlTypeMapper's bare-primitive switches), so the probe must hand
            // the mapper the well-formed applied shape the DamlTypeApp arms handle.
            1 => new DamlTypeApp(bare, [SingleProbeArgument(primitive)]),
            2 => new DamlTypeApp(bare,
            [
                new DamlPrimitiveType(DamlPrimitive.Text),
                new DamlPrimitiveType(DamlPrimitive.Int64),
            ]),
            _ => throw new InvalidOperationException(
                $"No probe shape is defined for a builtin of arity {arity} ({primitive})."),
        };
    }

    private static DamlType SingleProbeArgument(DamlPrimitive primitive) =>
        // Numeric's argument is the Nat pun: the scale arrives as a DamlTypeVar carrying it.
        primitive == DamlPrimitive.Numeric
            ? new DamlTypeVar("10")
            : new DamlPrimitiveType(DamlPrimitive.Text);

    private static DamlTypeMapper NewMapper()
    {
        var package = new DamlPackage
        {
            PackageId = "pkg-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Test.Module",
                    Templates = [],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };
        var options = new CodeGenOptions { NamespacePrefix = "Test.Package" };
        var context = PackageEmitContext.ForPackage(package, options, isMainPackage: true).Single();
        return new DamlTypeMapper(context, new StubResolver());
    }

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => typeRef.Name;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }
}
