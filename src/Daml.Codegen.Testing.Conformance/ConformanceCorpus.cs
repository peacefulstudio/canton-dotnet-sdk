// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Testing.Conformance;

/// <summary>
/// One of the Daml packages whose generated types this assembly compiles and whose
/// DAR it embeds.
/// </summary>
public enum ConformancePackage
{
    /// <summary>
    /// The type-shape corpus: primitives, collections, nominal types and the harder
    /// type-system corners, built at Daml-LF 2.1.
    /// </summary>
    RichTypes,

    /// <summary>
    /// The contract-key corpus: a record key built from several payload fields, a record
    /// whose field comes from a projection nested inside a payload record, a record built
    /// by a function declared in another module, and a bare <c>Party</c> key. Built at
    /// Daml-LF 2.3, the earliest 2.x version that can express a contract key.
    /// </summary>
    ContractKeys,

    /// <summary>
    /// A single template built with no Daml-LF target requested, so it carries whatever
    /// version damlc emits by default (2.3 as of Daml SDK 3.6.0), which is what a scaffolded
    /// project hands the toolchain.
    /// </summary>
    DefaultTarget,
}

/// <summary>
/// Access to the conformance corpus DARs embedded in this package. Consumers
/// upload these DARs to a participant before creating contracts of the corpus
/// templates.
/// </summary>
public static class ConformanceCorpus
{
    /// <summary>
    /// Opens a stream over the embedded <see cref="ConformancePackage.RichTypes"/> DAR.
    /// The caller owns the returned stream and must dispose it.
    /// </summary>
    public static Stream OpenDar() => OpenDar(ConformancePackage.RichTypes);

    /// <summary>
    /// Opens a stream over the embedded DAR of <paramref name="package"/>. The caller
    /// owns the returned stream and must dispose it.
    /// </summary>
    public static Stream OpenDar(ConformancePackage package)
    {
        var resourceName = ResourceNameOf(package);
        var assembly = typeof(ConformanceCorpus).Assembly;
        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded conformance DAR resource '{resourceName}' was not found.");
    }

    private static string ResourceNameOf(ConformancePackage package) => package switch
    {
        ConformancePackage.RichTypes => "richtypes.dar",
        ConformancePackage.ContractKeys => "contractkeys.dar",
        ConformancePackage.DefaultTarget => "defaulttarget.dar",
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "No conformance DAR is embedded for this package."),
    };
}
