// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class EmitterMachineryApiSurfaceTests
{
    public static TheoryData<Type> EmitterMachineryTypes() =>
        [
            typeof(ChoiceEmitter),
            typeof(RecordEmitter),
            typeof(TemplateEmitter),
            typeof(EnumEmitter),
            typeof(VariantEmitter),
            typeof(InterfaceEmitter),
            typeof(RecordSerializationEmitter),
            typeof(SubmissionExtensionsEmitter),
            typeof(DamlTypeMapper),
            typeof(PackageEmitContext),
            typeof(TypeReferenceQualifier),
            typeof(DarCrossPackageResolver),
            typeof(PartyAnalysis),
            typeof(DamlWrappedOptional),
            typeof(OptionalEncoding),
        ];

    [Theory]
    [MemberData(nameof(EmitterMachineryTypes))]
    public void EmitterMachineryApiSurface_emitter_machinery_type_is_internal(Type emitterType)
    {
        emitterType.IsPublic.Should().BeFalse(
            "{0} is codegen implementation detail used only within CodeGen/, and a public " +
            "surface would semver-lock it against refactors without giving consumers a " +
            "supported entry point",
            emitterType.Name);
    }
}
