// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins the dispatch discipline of the public type algebra: every <see cref="DamlType"/> node
/// reaches exactly its own <see cref="IDamlTypeVisitor{TResult}"/> arm, and exhaustiveness is
/// enforced by the compiler (CS0535 at every implementer) rather than by a runtime default —
/// so the sources carry no suppression of that warning and no discard arm in a switch over
/// <see cref="DamlType"/> that would silently absorb a node.
/// </summary>
public class DamlTypeVisitorTests
{
    private sealed class RecordingVisitor : IDamlTypeVisitor<string>
    {
        public List<string> VisitedArms { get; } = [];

        public string VisitPrimitive(DamlPrimitiveType type) => Record(nameof(VisitPrimitive));

        public string VisitTypeRef(DamlTypeRef type) => Record(nameof(VisitTypeRef));

        public string VisitTypeApp(DamlTypeApp type) => Record(nameof(VisitTypeApp));

        public string VisitTypeVar(DamlTypeVar type) => Record(nameof(VisitTypeVar));

        public string VisitList(DamlListType type) => Record(nameof(VisitList));

        public string VisitOptional(DamlOptionalType type) => Record(nameof(VisitOptional));

        public string VisitTextMap(DamlTextMapType type) => Record(nameof(VisitTextMap));

        public string VisitGenMap(DamlGenMapType type) => Record(nameof(VisitGenMap));

        public string VisitContractId(DamlContractIdType type) => Record(nameof(VisitContractId));

        private string Record(string arm)
        {
            VisitedArms.Add(arm);
            return arm;
        }
    }

    private sealed class ArmException(string arm) : Exception(arm);

    private sealed class ThrowingVisitor : IDamlTypeVisitor<object>
    {
        public object VisitPrimitive(DamlPrimitiveType type) => throw new ArmException(nameof(VisitPrimitive));

        public object VisitTypeRef(DamlTypeRef type) => throw new ArmException(nameof(VisitTypeRef));

        public object VisitTypeApp(DamlTypeApp type) => throw new ArmException(nameof(VisitTypeApp));

        public object VisitTypeVar(DamlTypeVar type) => throw new ArmException(nameof(VisitTypeVar));

        public object VisitList(DamlListType type) => throw new ArmException(nameof(VisitList));

        public object VisitOptional(DamlOptionalType type) => throw new ArmException(nameof(VisitOptional));

        public object VisitTextMap(DamlTextMapType type) => throw new ArmException(nameof(VisitTextMap));

        public object VisitGenMap(DamlGenMapType type) => throw new ArmException(nameof(VisitGenMap));

        public object VisitContractId(DamlContractIdType type) => throw new ArmException(nameof(VisitContractId));
    }

    public static TheoryData<DamlType, string> NodeArmPairs() => new()
    {
        { new DamlPrimitiveType(DamlPrimitive.Int64), "VisitPrimitive" },
        { new DamlTypeRef("pkg-id", "Test.Module", "Widget"), "VisitTypeRef" },
        {
            new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Int64)]),
            "VisitTypeApp"
        },
        { new DamlTypeVar("a"), "VisitTypeVar" },
        { new DamlListType(new DamlPrimitiveType(DamlPrimitive.Int64)), "VisitList" },
        { new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text)), "VisitOptional" },
        { new DamlTextMapType(new DamlPrimitiveType(DamlPrimitive.Int64)), "VisitTextMap" },
        {
            new DamlGenMapType(
                new DamlPrimitiveType(DamlPrimitive.Text),
                new DamlPrimitiveType(DamlPrimitive.Int64)),
            "VisitGenMap"
        },
        {
            new DamlContractIdType(new DamlTypeRef("pkg-id", "Test.Module", "Widget")),
            "VisitContractId"
        },
    };

    [Theory]
    [MemberData(nameof(NodeArmPairs))]
    public void DamlType_Accept_dispatches_each_node_to_exactly_its_matching_arm(DamlType node, string expectedArm)
    {
        var visitor = new RecordingVisitor();

        var visitedArm = node.Accept(visitor);

        visitedArm.Should().Be(expectedArm,
            "Accept must return the value the node's own arm produced");
        visitor.VisitedArms.Should().Equal([expectedArm],
            "each node must reach exactly its own arm — no other arm and no double invocation");
    }

    [Theory]
    [MemberData(nameof(NodeArmPairs))]
    public void DamlType_Accept_raises_the_arm_specific_failure_for_each_node(DamlType node, string expectedArm)
    {
        var act = () => node.Accept(new ThrowingVisitor());

        act.Should().Throw<ArmException>().WithMessage(expectedArm,
            "an arm that throws its own named failure proves dispatch reaches that arm and never "
            + "falls through to a shared default");
    }

    [Fact]
    public void DamlTypeVisitor_dispatch_exhaustiveness_is_never_suppressed()
    {
        var offenders = SourceFiles()
            .Select(path => (Path: path, Lines: File.ReadAllLines(path)))
            .SelectMany(file => file.Lines
                .Where(IsSuppressedExhaustivenessCheck)
                .Select(line => $"{Path.GetFileName(file.Path)}: {line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty(
            "CS0535 (unimplemented interface member) is the compiler-enforced checklist that a "
            + "visitor implementation handles every DamlType node, and CS8509 (unhandled enum case) "
            + "is the same checklist for the DamlPrimitive enum switches — suppressing either turns "
            + "a missing arm into a silent default at runtime. Only CS8524 (the out-of-range cast) "
            + "may be suppressed, and only on an otherwise-exhaustive switch");
    }

    /// <summary>
    /// The ratchet the emitter's visitor migration drove to zero: the pre-migration shape
    /// switches carried exactly fifteen discard arms (DamlTypeMapper 9, OptionalRepresentation 3,
    /// ChoiceEmitter.NonContractExercisers 3), and the migration removed them all. The table is
    /// now empty, so any discard arm in a switch over a <see cref="DamlType"/> node — anywhere,
    /// in either project — is a new hole that silently absorbs an unknown node.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> PreMigrationDiscardArmAllowlist =
        new Dictionary<string, int>();

    [Fact]
    public void DamlTypeVisitor_node_switch_discard_arms_are_confined_to_the_pre_migration_set()
    {
        var discardArms = DamlTypeSwitchDiscardArms()
            .GroupBy(arm => arm.File)
            .ToDictionary(group => group.Key, group => group.Count());

        discardArms.Should().BeEquivalentTo(PreMigrationDiscardArmAllowlist,
            "a discard arm in a switch over DamlType silently absorbs any node the switch does not "
            + "name, defeating the CS0535 exhaustiveness the visitor enforces. The emitter's visitor "
            + "migration removed the fifteen pre-migration arms, so the allowlist is empty: any "
            + "discard arm in a switch over a DamlType node is a new hole");
    }

    private static bool IsSuppressedExhaustivenessCheck(string line) =>
        line.Contains("#pragma warning disable", StringComparison.Ordinal)
        && (line.Contains("CS0535", StringComparison.Ordinal)
            || line.Contains("CS8509", StringComparison.Ordinal));

    private static IEnumerable<(string File, int Line)> DamlTypeSwitchDiscardArms()
    {
        foreach (var path in SourceFiles())
        {
            var lines = File.ReadAllLines(path);
            for (var arm = 0; arm < lines.Length; arm++)
            {
                var trimmed = lines[arm].TrimStart();
                if (!trimmed.StartsWith("_ =>", StringComparison.Ordinal)
                    && !trimmed.StartsWith("_ when", StringComparison.Ordinal))
                {
                    continue;
                }
                if (IsInsideDamlTypeSwitch(lines, arm))
                {
                    yield return (Path.GetFileName(path), arm + 1);
                }
            }
        }
    }

    private static bool IsInsideDamlTypeSwitch(string[] lines, int arm)
    {
        for (var line = arm - 1; line >= 0 && line >= arm - 200; line--)
        {
            if (lines[line].Contains("switch", StringComparison.Ordinal))
            {
                return SwitchMentionsANode(lines, line, arm);
            }
            var candidate = lines[line].TrimStart();
            var isMemberSignature = (candidate.StartsWith("private ", StringComparison.Ordinal)
                                     || candidate.StartsWith("internal ", StringComparison.Ordinal)
                                     || candidate.StartsWith("public ", StringComparison.Ordinal)
                                     || candidate.StartsWith("protected ", StringComparison.Ordinal))
                                    && !lines[line].Contains("=>", StringComparison.Ordinal);
            if (isMemberSignature)
            {
                return false;
            }
        }
        return false;
    }

    private static bool SwitchMentionsANode(string[] lines, int switchLine, int arm)
    {
        for (var line = switchLine; line <= arm; line++)
        {
            var text = lines[line];
            var arrow = text.IndexOf("=>", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                text = text[..arrow];
            }
            if (NodeNames().Any(name => Regex.IsMatch(text, $@"\b{name}\b")))
            {
                return true;
            }
        }
        return false;
    }

    private static string[] NodeNames() =>
    [
        nameof(DamlPrimitiveType),
        nameof(DamlTypeApp),
        nameof(DamlTypeRef),
        nameof(DamlTypeVar),
        nameof(DamlWrappedOptional),
        nameof(DamlListType),
        nameof(DamlOptionalType),
        nameof(DamlTextMapType),
        nameof(DamlGenMapType),
        nameof(DamlContractIdType),
    ];

    private static IEnumerable<string> SourceFiles()
    {
        var repoRoot = LocateRepoRoot();
        string[] roots =
        [
            Path.Combine(repoRoot, "src", "Daml.Codegen.Intermediate"),
            Path.Combine(repoRoot, "src", "Daml.Codegen.CSharp"),
        ];
        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Daml.Codegen.CSharp.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot locate repo root from {AppContext.BaseDirectory}. "
            + "Expected Daml.Codegen.CSharp.slnx at the repository root.");
    }
}
