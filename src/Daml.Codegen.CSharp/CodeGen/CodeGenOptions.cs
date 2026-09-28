// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Configuration options for the C# code generator.
/// </summary>
public sealed class CodeGenOptions
{
    /// <summary>
    /// Gets or sets the namespace prefix for the main package's generated code. Every Daml
    /// module is emitted into a C# namespace named after the module; when this is set, the
    /// main package's modules are emitted under <c>{NamespacePrefix}.{Module}</c>, except a
    /// module whose name already equals or starts with the prefix at a segment boundary,
    /// which keeps its own name. Dependency packages emitted with
    /// <see cref="IncludeDependencies"/> are never prefixed, so a locally generated
    /// dependency is interchangeable with the same package's published bindings. If null,
    /// the namespace is the module name.
    /// </summary>
    public string? NamespacePrefix { get; init; }

    /// <summary>
    /// Gets or sets a regex pattern to filter which templates to generate.
    /// Default is ".*" (all templates).
    /// </summary>
    public string? RootFilter { get; init; }

    /// <summary>
    /// Gets or sets whether to enable nullable reference types.
    /// </summary>
    public bool EnableNullableReferenceTypes { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate XML documentation comments.
    /// </summary>
    public bool GenerateXmlDocs { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to use file-scoped namespaces.
    /// </summary>
    public bool UseFileScopedNamespaces { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate a .csproj project file.
    /// </summary>
    public bool GenerateProjectFile { get; init; }

    /// <summary>
    /// Gets or sets whether to include dependency packages in code generation.
    /// </summary>
    public bool IncludeDependencies { get; init; }

    /// <summary>
    /// Gets or sets the target framework for the generated project (e.g., "net10.0").
    /// </summary>
    public string TargetFramework { get; init; } = "net10.0";

    /// <summary>
    /// Gets or sets the version of the Daml.Runtime package to reference.
    /// When null, the generated project pins the Daml.Runtime version that is
    /// lockstep-versioned with this emitter build.
    /// </summary>
    public string? RuntimePackageVersion { get; init; }

    /// <summary>
    /// Gets or sets the 4th-segment emitter counter for the generated NuGet
    /// version. The Daml package supplies segments 1–3
    /// (<c>Major.Minor.Patch</c>); segment 4 is a
    /// monotonic counter keyed to the emitter version that produced this
    /// build. Defaults to <c>0</c> for the first emission of a given content.
    /// A release pipeline supplies a monotonic counter to distinguish
    /// republished builds of the same source; local-dev codegen invocations
    /// leave the default in place.
    /// </summary>
    public int EmitterCounter { get; init; }

    /// <summary>
    /// Gets or sets the SPDX license expression emitted in the generated
    /// <c>.csproj</c>'s <c>&lt;PackageLicenseExpression&gt;</c>. Defaults to
    /// <c>Apache-2.0</c>, correct for any DAR whose own licensing is
    /// Apache-2.0. Consumers running the
    /// emitter against a proprietary or differently-licensed DAR should
    /// pass the right SPDX identifier here (or via <c>--package-license</c>
    /// on the CLI) so the published <c>.nuspec</c> doesn't misrepresent
    /// the source DAR's license.
    /// </summary>
    public string PackageLicenseExpression { get; init; } = "Apache-2.0";

    /// <summary>
    /// Gets or sets the SemVer prerelease suffix appended to the generated
    /// package <c>&lt;Version&gt;</c> (e.g. <c>preview.2</c>, producing
    /// <c>0.1.6.1-preview.2</c>). Stored without the leading dash; empty, null,
    /// or whitespace means no suffix. Mirrors the emitter's own prerelease tag.
    /// Affects only the generated package version; the
    /// <see cref="RuntimePackageVersion"/> reference is unaffected.
    /// </summary>
    public string? VersionSuffix { get; init; }

    /// <summary>
    /// Gets or sets whether this run also produces the NuGet packages for the other
    /// DAR packages the generated code references. When <c>true</c>, each such
    /// reference is pinned to the exact version this run produces for it — the same
    /// <see cref="EmitterCounter"/> and <see cref="VersionSuffix"/> as the main
    /// package — which is correct only for a release run that publishes the whole
    /// family together under one ordinal. When <c>false</c> (the default), the
    /// generation ordinal and prerelease tag a dependency was published with are
    /// facts about that publication and are not recoverable from the DAR, so the
    /// reference instead floats over every generation and prerelease published
    /// against the dependency's intrinsic <c>Major.Minor.Patch</c>. On the CLI this
    /// is set by <c>--release-counters</c>, the flag the release pipeline uses to
    /// key one generation ordinal across the whole family.
    /// </summary>
    public bool PublishesReferencedPackages { get; init; }

    /// <summary>
    /// Gets or sets the repository URL emitted in the generated <c>.csproj</c>'s
    /// <c>&lt;PackageProjectUrl&gt;</c>, <c>&lt;RepositoryUrl&gt;</c>, and
    /// <c>&lt;RepositoryType&gt;</c>. When null, empty, or whitespace, those three
    /// elements are omitted entirely rather than defaulting to a wrong value —
    /// third-party emitter users must supply their own URL (or via
    /// <c>--repository-url</c> on the CLI) so the published package never points at
    /// an unrelated repository. Independent of the generator-tool attribution link
    /// in the README, which always identifies this codegen tool.
    /// </summary>
    public string? RepositoryUrl { get; init; }
}

/// <summary>
/// Represents a generated output file — either a text file (created with
/// <see cref="Text"/>) or a binary file such as the package icon (created with
/// <see cref="Binary"/>). The two are mutually exclusive and constructed only
/// through those factories: a text file leaves <see cref="BinaryContent"/> null,
/// a binary file leaves <see cref="Content"/> empty.
/// </summary>
public sealed record GeneratedFile
{
    private GeneratedFile(string relativePath, string content, byte[]? binaryContent)
    {
        RelativePath = relativePath;
        Content = content;
        BinaryContent = binaryContent;
    }

    /// <summary>The path relative to the output directory, '/'-separated on every platform.</summary>
    public string RelativePath { get; }

    /// <summary>The text content, or empty for a binary file.</summary>
    public string Content { get; }

    /// <summary>The raw bytes for a binary file, or null for a text file.</summary>
    public byte[]? BinaryContent { get; }

    /// <summary>True when this file carries binary bytes rather than text.</summary>
    public bool IsBinary => BinaryContent is not null;

    /// <summary>Creates a text file at <paramref name="relativePath"/>.</summary>
    public static GeneratedFile Text(string relativePath, string content) =>
        new(relativePath, content, null);

    /// <summary>
    /// Creates a binary file at <paramref name="relativePath"/> carrying
    /// <paramref name="binaryContent"/>. The bytes must be non-empty.
    /// </summary>
    public static GeneratedFile Binary(string relativePath, byte[] binaryContent)
    {
        if (binaryContent is null || binaryContent.Length == 0)
        {
            throw new ArgumentException("A binary generated file must carry non-empty content.", nameof(binaryContent));
        }
        return new GeneratedFile(relativePath, string.Empty, binaryContent.ToArray());
    }
}
