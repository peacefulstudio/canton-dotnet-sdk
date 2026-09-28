// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The submitter parameter an emitted <c>Try&lt;Choice&gt;Async</c> method carries: the
/// <c>SubmitterInfo</c> shape, which expresses both <c>actAs</c> and <c>readAs</c>, so a
/// submitter that must read contracts it does not act as stays expressible through the
/// generated surface instead of dropping to a hand-built <c>ExerciseCommand</c>. A single
/// <c>Party</c> reaches the same method through the implicit conversion.
/// </summary>
/// <param name="TypeName">The qualified C# type of the parameter.</param>
/// <param name="Name">The parameter name, reused as its <c>&lt;param&gt;</c> doc tag.</param>
/// <param name="DocSummary">The <c>&lt;param&gt;</c> doc text.</param>
internal sealed record ChoiceSubmitterParameter(
    string TypeName,
    string Name,
    string DocSummary);
