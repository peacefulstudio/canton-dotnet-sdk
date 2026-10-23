// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Google.Protobuf.Reflection;
using Daml.Codegen.Intermediate;

namespace Daml.Codegen.DarParser.Cli.Tests;

internal static class IntermediateDarByteComparison
{
    public static string? DescribeFirstDifference(byte[] oracle, byte[] actual)
    {
        if (oracle.AsSpan().SequenceEqual(actual)) return null;

        var oracleDar = IntermediateDar.Parser.ParseFrom(oracle);
        var actualDar = IntermediateDar.Parser.ParseFrom(actual);
        var fieldPath = FirstDifferingFieldPath("", oracleDar, actualDar);
        if (fieldPath is not null) return fieldPath;

        var offset = FirstDifferingOffset(oracle, actual);
        return $"no differing field; the encodings first differ at byte offset {offset} " +
               $"(oracle {oracle.Length} bytes, actual {actual.Length} bytes)";
    }

    private static string? FirstDifferingFieldPath(string path, IMessage oracle, IMessage actual)
    {
        foreach (var field in oracle.Descriptor.Fields.InFieldNumberOrder())
        {
            var fieldPath = path.Length == 0 ? field.JsonName : $"{path}.{field.JsonName}";
            var difference = field.IsRepeated
                ? FirstDifferingRepeatedElement(fieldPath, field, oracle, actual)
                : FirstDifferingSingularValue(fieldPath, field, oracle, actual);
            if (difference is not null) return difference;
        }
        return null;
    }

    private static string? FirstDifferingSingularValue(string fieldPath, FieldDescriptor field, IMessage oracle, IMessage actual)
    {
        if (field.FieldType == FieldType.Message || field.ContainingOneof is not null)
        {
            var oracleHasValue = field.Accessor.HasValue(oracle);
            var actualHasValue = field.Accessor.HasValue(actual);
            if (oracleHasValue != actualHasValue)
                return $"{fieldPath}: oracle {(oracleHasValue ? "set" : "unset")}, actual {(actualHasValue ? "set" : "unset")}";
            if (!oracleHasValue) return null;
        }

        var oracleValue = field.Accessor.GetValue(oracle);
        var actualValue = field.Accessor.GetValue(actual);
        if (field.FieldType == FieldType.Message)
            return FirstDifferingFieldPath(fieldPath, (IMessage)oracleValue, (IMessage)actualValue);
        return Equals(oracleValue, actualValue)
            ? null
            : $"{fieldPath}: oracle {Render(oracleValue)}, actual {Render(actualValue)}";
    }

    private static string? FirstDifferingRepeatedElement(string fieldPath, FieldDescriptor field, IMessage oracle, IMessage actual)
    {
        var oracleItems = ((System.Collections.IList)field.Accessor.GetValue(oracle)).Cast<object>().ToList();
        var actualItems = ((System.Collections.IList)field.Accessor.GetValue(actual)).Cast<object>().ToList();

        for (var index = 0; index < Math.Min(oracleItems.Count, actualItems.Count); index++)
        {
            var elementPath = $"{fieldPath}[{index}{ElementName(oracleItems[index])}]";
            if (oracleItems[index] is IMessage oracleMessage)
            {
                var difference = FirstDifferingFieldPath(elementPath, oracleMessage, (IMessage)actualItems[index]);
                if (difference is not null) return difference;
            }
            else if (!Equals(oracleItems[index], actualItems[index]))
            {
                return $"{elementPath}: oracle {Render(oracleItems[index])}, actual {Render(actualItems[index])}";
            }
        }

        return oracleItems.Count == actualItems.Count
            ? null
            : $"{fieldPath}: oracle has {oracleItems.Count} elements, actual has {actualItems.Count}";
    }

    private static string ElementName(object element)
    {
        if (element is not IMessage message) return "";
        var nameField = message.Descriptor.FindFieldByName("name");
        return nameField is { FieldType: FieldType.String, IsRepeated: false }
            ? $":{nameField.Accessor.GetValue(message)}"
            : "";
    }

    private static int FirstDifferingOffset(byte[] oracle, byte[] actual)
    {
        var shared = Math.Min(oracle.Length, actual.Length);
        for (var offset = 0; offset < shared; offset++)
        {
            if (oracle[offset] != actual[offset]) return offset;
        }
        return shared;
    }

    private static string Render(object? value) => value switch
    {
        null => "<null>",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? "<null>",
    };
}
