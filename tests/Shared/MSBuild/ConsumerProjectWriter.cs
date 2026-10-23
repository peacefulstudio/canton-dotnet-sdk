// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// Writes the throwaway <c>TestConsumer.csproj</c> that the MSBuild adapter
/// integration tests build. Every knob is optional so an omitted argument leaves
/// the corresponding property out of the project file entirely — which is how the
/// tests assert an adapter's defaults rather than an explicitly set value.
/// </summary>
internal static class ConsumerProjectWriter
{
    internal static string Write(
        string projectDir,
        string propsFile,
        string targetsFile,
        string? generatedDir = null,
        IReadOnlyList<string>? darPaths = null,
        string? damlCodegenMode = null,
        string? damlCodegenBundleVersion = null,
        string? damlCodegenBundleDigest = null,
        string? damlCodegenOciRepository = null,
        string? damlCodegenTool = null,
        string? damlCodegenEmitterTool = null,
        bool includeDependencies = false,
        string? itemNamespace = null,
        bool? itemIncludeDependencies = null,
        IReadOnlyList<DamlProjectItem>? damlProjects = null)
    {
        var itemMetadata = ItemMetadataAttributes(itemNamespace, itemIncludeDependencies);
        var archiveItems = string.Join('\n', (darPaths ?? []).Select(dar =>
            $"""
                  <DamlArchive Include="{EscapeXml(dar)}"{itemMetadata} />
            """));

        var projectItems = string.Join('\n', (damlProjects ?? []).Select(project =>
            $"""
                  <DamlProject Include="{EscapeXml(project.Include)}"{DataDependenciesAttribute(project)} />
            """));

        var properties = string.Join('\n', new[]
        {
            Property("DamlGeneratedDir", generatedDir),
            Property("DamlCodegenMode", damlCodegenMode),
            Property("DamlCodegenBundleVersion", damlCodegenBundleVersion),
            Property("DamlCodegenBundleDigest", damlCodegenBundleDigest),
            Property("DamlCodegenOciRepository", damlCodegenOciRepository),
            Property("DamlCodegenTool", damlCodegenTool),
            Property("DamlCodegenEmitterTool", damlCodegenEmitterTool),
            Property("DamlIncludeDependencies", includeDependencies ? "true" : null),
        }.Where(element => element.Length > 0).Select(element => $"    {element}"));

        var csprojPath = Path.Combine(projectDir, "TestConsumer.csproj");
        var content = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{EscapeXml(propsFile)}" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
                <Nullable>enable</Nullable>
            {properties}
              </PropertyGroup>
              <ItemGroup>
            {archiveItems}
            {projectItems}
              </ItemGroup>
              <Import Project="{EscapeXml(targetsFile)}" />
            </Project>
            """;
        File.WriteAllText(csprojPath, content);
        return csprojPath;
    }

    private static string DataDependenciesAttribute(DamlProjectItem project) =>
        project.DataDependencies is null
            ? string.Empty
            : $" DataDependencies=\"{EscapeXml(string.Join(';', project.DataDependencies))}\"";

    private static string Property(string name, string? value) =>
        value is null ? string.Empty : $"<{name}>{EscapeXml(value)}</{name}>";

    private static string ItemMetadataAttributes(string? itemNamespace, bool? itemIncludeDependencies)
    {
        var attributes = new[]
        {
            itemNamespace is null ? null : $"Namespace=\"{EscapeXml(itemNamespace)}\"",
            itemIncludeDependencies is null ? null : $"IncludeDependencies=\"{(itemIncludeDependencies.Value ? "true" : "false")}\"",
        }.Where(attribute => attribute is not null);
        var joined = string.Join(' ', attributes);
        return joined.Length == 0 ? string.Empty : $" {joined}";
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

/// <summary>
/// One <c>&lt;DamlProject&gt;</c> item of a test consumer: the directory holding
/// <c>daml.yaml</c>, relative to the consumer project, and its optional
/// <c>DataDependencies</c> metadata.
/// </summary>
internal sealed record DamlProjectItem(string Include, IReadOnlyList<string>? DataDependencies = null);
