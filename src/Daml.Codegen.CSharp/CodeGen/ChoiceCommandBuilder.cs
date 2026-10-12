// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>The XML documentation a command builder is emitted with.</summary>
/// <param name="Summary">The lines of the <c>summary</c> element.</param>
/// <param name="ReceiverParameter">The <c>param</c> element describing the receiver, verbatim.</param>
/// <param name="Remarks">The lines of the <c>remarks</c> element; none omits the element.</param>
internal sealed record CommandBuilderDocs(
    IReadOnlyList<string> Summary,
    string ReceiverParameter,
    IReadOnlyList<string> Remarks);

/// <summary>
/// What varies between the generated static methods that build a choice's command without
/// submitting it: the contract-id, by-key and interface-typed builders all share one skeleton.
/// </summary>
/// <param name="CommandType">The qualified type of the command the builder returns.</param>
/// <param name="Name">The builder's method name.</param>
/// <param name="Docs">The XML documentation, or null when none is emitted.</param>
/// <param name="ReceiverParameter">The first parameter, verbatim, including <c>this</c> when it extends.</param>
/// <param name="ArgumentParameterType">The type of the <c>argument</c> parameter; null when the choice takes none.</param>
/// <param name="NullGuardedParameters">The parameters that get an <c>ArgumentNullException.ThrowIfNull</c>, in order.</param>
/// <param name="ReturnStatement">
/// The return statement's lines; the first is written at the body's indentation, every further
/// one a level deeper.
/// </param>
internal sealed record ChoiceCommandBuilder(
    string CommandType,
    string Name,
    CommandBuilderDocs? Docs,
    string ReceiverParameter,
    string? ArgumentParameterType,
    IReadOnlyList<string> NullGuardedParameters,
    IReadOnlyList<string> ReturnStatement);
