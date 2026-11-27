// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class AbsenceBindsAnUnstatedValueAttribute : Attribute
{
    internal static bool IsDefinedOn(Type type)
    {
        if (type.ContainsGenericParameters)
        {
            return false;
        }

        var declaration = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
        return declaration.IsDefined(typeof(AbsenceBindsAnUnstatedValueAttribute), inherit: false);
    }
}
