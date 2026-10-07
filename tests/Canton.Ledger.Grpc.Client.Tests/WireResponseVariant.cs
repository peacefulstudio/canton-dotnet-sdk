// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;
using Type = System.Type;

namespace Canton.Ledger.Grpc.Client.Tests;

internal enum ScalarFill
{
    Default,
    Whitespace,
    Plausible,
    Hostile,
}

internal sealed record WireResponseVariant(
    string Name,
    bool Populated,
    ScalarFill Scalars,
    int OneofRound,
    int HoleOrdinal = -1,
    ScalarFill HoleScalars = ScalarFill.Default)
{
    private const int MaxDepth = 6;
    private const int WholeResponseOneofRounds = 20;
    private const int HoleOneofRounds = 10;

    internal static IReadOnlyList<WireResponseVariant> All { get; } = BuildAll();

    internal IMessage Build(Type messageType) => new Builder(this).Create(messageType, depth: 0);

    internal static int FieldCount(Type messageType, int oneofRound) =>
        Builder.Count(new WireResponseVariant("count", Populated: true, ScalarFill.Plausible, oneofRound), messageType);

    private static List<WireResponseVariant> BuildAll()
    {
        var variants = new List<WireResponseVariant> { new("bare", Populated: false, ScalarFill.Default, OneofRound: 0) };
        foreach (var scalars in Enum.GetValues<ScalarFill>())
        {
            for (var round = 0; round < WholeResponseOneofRounds; round++)
            {
                variants.Add(new WireResponseVariant($"populated-{scalars}-oneof{round}", Populated: true, scalars, round));
            }
        }

        var holeCount = ResponseTypes().Max(type => Enumerable.Range(0, HoleOneofRounds).Max(round => FieldCount(type, round)));
        foreach (var holeFill in new[] { ScalarFill.Default, ScalarFill.Whitespace, ScalarFill.Hostile })
        {
            for (var round = 0; round < HoleOneofRounds; round++)
            {
                for (var ordinal = 0; ordinal < holeCount; ordinal++)
                {
                    variants.Add(new WireResponseVariant(
                        $"plausible-hole{ordinal}-{holeFill}-oneof{round}", Populated: true, ScalarFill.Plausible, round, ordinal, holeFill));
                }
            }
        }

        return variants;
    }

    private static IEnumerable<Type> ResponseTypes() =>
        typeof(Com.Daml.Ledger.Api.V2.GetLedgerEndResponse).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Response", StringComparison.Ordinal) && typeof(IMessage).IsAssignableFrom(type));

    private sealed class Builder(WireResponseVariant variant)
    {
        private int _ordinal = -1;

        internal static int Count(WireResponseVariant variant, Type messageType)
        {
            var builder = new Builder(variant);
            builder.Create(messageType, depth: 0);
            return builder._ordinal + 1;
        }

        internal IMessage Create(Type messageType, int depth)
        {
            var message = (IMessage)Activator.CreateInstance(messageType)!;
            if (!variant.Populated || depth >= MaxDepth)
            {
                return message;
            }

            foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
            {
                if (IsUnselectedOneofCase(field))
                {
                    continue;
                }

                Populate(message, field, depth);
            }

            return message;
        }

        private bool IsUnselectedOneofCase(FieldDescriptor field) =>
            field.RealContainingOneof is { } oneof
            && oneof.Fields[variant.OneofRound % oneof.Fields.Count] != field;

        private void Populate(IMessage message, FieldDescriptor field, int depth)
        {
            _ordinal++;
            var isHole = _ordinal == variant.HoleOrdinal;
            var fill = isHole ? variant.HoleScalars : variant.Scalars;
            if (isHole && fill == ScalarFill.Default && field.FieldType == FieldType.Message)
            {
                return;
            }

            if (!isHole && fill == ScalarFill.Plausible && PlausibleOverlayFor(field) is { } overlay)
            {
                Assign(message, field, overlay);
            }
            else if (field.IsMap)
            {
                var entry = field.MessageType.Fields.InFieldNumberOrder();
                ((IDictionary)field.Accessor.GetValue(message)).Add(ValueFor(entry[0], fill, depth), ValueFor(entry[1], fill, depth));
            }
            else if (field.IsRepeated)
            {
                ((IList)field.Accessor.GetValue(message)).Add(ValueFor(field, fill, depth));
            }
            else
            {
                field.Accessor.SetValue(message, ValueFor(field, fill, depth));
            }
        }

        private static object? PlausibleOverlayFor(FieldDescriptor field)
        {
            if (field.FieldType == FieldType.Message && field.MessageType.ClrType == typeof(Com.Daml.Ledger.Api.V2.Identifier))
            {
                return new Com.Daml.Ledger.Api.V2.Identifier { PackageId = "test-pkg", ModuleName = "Sample.Foo", EntityName = "FooBar" };
            }

            return field.Name switch
            {
                "create_arguments" => LedgerClientTestFixtures.OwnerArgumentsFor("alice::ns1"),
                "next_page_token" => field.FieldType == FieldType.Bytes ? ByteString.Empty : string.Empty,
                _ => null,
            };
        }

        private static void Assign(IMessage message, FieldDescriptor field, object value)
        {
            if (field.IsRepeated)
            {
                ((IList)field.Accessor.GetValue(message)).Add(value);
            }
            else
            {
                field.Accessor.SetValue(message, value);
            }
        }

        private object ValueFor(FieldDescriptor field, ScalarFill fill, int depth) => field.FieldType switch
        {
            FieldType.Message => MessageValueFor(field.MessageType.ClrType, fill, depth),
            FieldType.String => fill switch
            {
                ScalarFill.Default => string.Empty,
                ScalarFill.Whitespace => " ",
                _ => "x",
            },
            FieldType.Bytes => fill == ScalarFill.Default ? ByteString.Empty : ByteString.CopyFrom(1),
            FieldType.Bool => fill is ScalarFill.Plausible or ScalarFill.Hostile,
            FieldType.Enum => Enum.ToObject(field.EnumType.ClrType, fill switch
            {
                ScalarFill.Plausible => 1,
                ScalarFill.Hostile => 9999,
                _ => 0,
            }),
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => fill switch
            {
                ScalarFill.Plausible => 1,
                ScalarFill.Hostile => -1,
                _ => 0,
            },
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => fill switch
            {
                ScalarFill.Plausible => 1L,
                ScalarFill.Hostile => -1L,
                _ => 0L,
            },
            FieldType.UInt32 or FieldType.Fixed32 => fill switch
            {
                ScalarFill.Plausible => 1U,
                ScalarFill.Hostile => uint.MaxValue,
                _ => 0U,
            },
            FieldType.UInt64 or FieldType.Fixed64 => fill switch
            {
                ScalarFill.Plausible => 1UL,
                ScalarFill.Hostile => ulong.MaxValue,
                _ => 0UL,
            },
            FieldType.Double => fill == ScalarFill.Hostile ? double.NaN : 0d,
            FieldType.Float => fill == ScalarFill.Hostile ? float.NaN : 0f,
            _ => throw new NotSupportedException($"Unhandled protobuf field type {field.FieldType}."),
        };

        private object MessageValueFor(Type messageType, ScalarFill fill, int depth)
        {
            if (messageType == typeof(Timestamp))
            {
                return new Timestamp { Seconds = fill == ScalarFill.Hostile ? long.MaxValue : 0 };
            }

            if (messageType == typeof(Duration))
            {
                return new Duration { Seconds = fill == ScalarFill.Hostile ? long.MaxValue : 0 };
            }

            return Create(messageType, depth + 1);
        }
    }
}
