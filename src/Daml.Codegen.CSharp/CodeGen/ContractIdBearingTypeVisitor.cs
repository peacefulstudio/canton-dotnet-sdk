// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed class ContractIdBearingTypeVisitor : IDamlTypeVisitor<bool>
{
    public static readonly ContractIdBearingTypeVisitor Instance = new();

    private ContractIdBearingTypeVisitor()
    {
    }

    public bool VisitPrimitive(DamlPrimitiveType type) => false;

    public bool VisitTypeRef(DamlTypeRef type) => false;

    public bool VisitTypeVar(DamlTypeVar type) => false;

    public bool VisitTextMap(DamlTextMapType type) => false;

    public bool VisitGenMap(DamlGenMapType type) => false;

    public bool VisitContractId(DamlContractIdType type) => true;

    public bool VisitOptional(DamlOptionalType type) => type.Value.Accept(this);

    public bool VisitList(DamlListType type) => type.Element.Accept(this);

    public bool VisitTypeApp(DamlTypeApp type)
    {
        if (type.Base is DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } && type.Arguments.Count == 1)
        {
            return true;
        }

        if (type.Base is DamlPrimitiveType { Primitive: DamlPrimitive.Optional or DamlPrimitive.List } && type.Arguments.Count == 1)
        {
            return type.Arguments[0].Accept(this);
        }

        if (type.Base is DamlTypeRef { Module: "DA.Types" } tuple && tuple.Name.StartsWith("Tuple", StringComparison.Ordinal))
        {
            return type.Arguments.Any(argument => argument.Accept(this));
        }

        return false;
    }
}
