// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Std = Daml.Runtime.Stdlib;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private const int FirstTupleArity = 2;
    private const int LastTupleArity = 20;

    private static void AddStdlib(Dictionary<Type, object[]> samples)
    {
        AddStdlibEnums(samples);
        AddStdlibRecords(samples);
        AddStdlibUnions(samples);
        AddStdlibCollections(samples);
        AddStdlibTuples(samples);
    }

    private static void AddStdlibEnums(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Std.DayOfWeek)] = [Std.DayOfWeek.Thursday];
        samples[typeof(Std.Month)] = [Std.Month.Oct];
        samples[typeof(Std.Ordering)] = [Std.Ordering.GT];
    }

    private static void AddStdlibRecords(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Std.RelTime)] = [new Std.RelTime(1_500_000)];
        samples[typeof(Std.All)] = [new Std.All(true)];
        samples[typeof(Std.Any)] = [new Std.Any(true)];
        samples[typeof(Std.Archive)] = [new Std.Archive()];
        samples[typeof(Std.ArithmeticError)] = [new Std.ArithmeticError("division by zero")];
        samples[typeof(Std.AssertionFailed)] = [new Std.AssertionFailed("assertion failed")];
        samples[typeof(Std.GeneralError)] = [new Std.GeneralError("general failure")];
        samples[typeof(Std.PreconditionFailed)] = [new Std.PreconditionFailed("precondition failed")];
        samples[typeof(Std.SrcLoc)] = [new Std.SrcLoc("pkg", "Module", "Module.daml", 10, 3, 12, 8)];
        samples[typeof(Std.Down<long>)] = [new Std.Down<long>(5)];
        samples[typeof(Std.Max<long>)] = [new Std.Max<long>(6)];
        samples[typeof(Std.Min<long>)] = [new Std.Min<long>(4)];
        samples[typeof(Std.Product<long>)] = [new Std.Product<long>(7)];
        samples[typeof(Std.Sum<long>)] = [new Std.Sum<long>(8)];
        samples[typeof(Std.Unit<long>)] = [new Std.Unit<long>(9)];
    }

    private static void AddStdlibUnions(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Std.Either<string, long>)] = [new Std.Either<string, long>.Left("left"), new Std.Either<string, long>.Right(7)];
        samples[typeof(Std.Optional<long>)] = [new Std.Optional<long>.Some(7), new Std.Optional<long>.None()];
        samples[typeof(Std.Minstd)] = [new Std.Minstd.Minstd_(11)];
        samples[typeof(Std.Formula<string>)] =
        [
            new Std.Formula<string>.Proposition("p"),
            new Std.Formula<string>.Negation(new Std.Formula<string>.Proposition("q")),
            new Std.Formula<string>.Conjunction([new Std.Formula<string>.Proposition("a"), new Std.Formula<string>.Proposition("b")]),
            new Std.Formula<string>.Disjunction([new Std.Formula<string>.Proposition("c"), new Std.Formula<string>.Proposition("d")]),
        ];
        samples[typeof(Std.Validation<string, long>)] =
        [
            new Std.Validation<string, long>.Errors(new Std.NonEmpty<string>("e1", ["e2", "e3"])),
            new Std.Validation<string, long>.Success(12),
        ];
    }

    private static void AddStdlibCollections(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Std.Map<string, long>)] =
        [
            new Std.Map<string, long>([new KeyValuePair<string, long>("a", 1), new KeyValuePair<string, long>("b", 2)]),
        ];
        samples[typeof(Std.NonEmpty<string>)] = [new Std.NonEmpty<string>("head", ["second", "third"])];
        samples[typeof(Std.Set<string>)] = [new Std.Set<string>(["x", "y", "z"])];
    }

    private static void AddStdlibTuples(Dictionary<Type, object[]> samples)
    {
        for (var arity = FirstTupleArity; arity <= LastTupleArity; arity++)
        {
            var sample = TupleOf(arity);
            samples[sample.GetType()] = [sample];
        }
    }

    private static object TupleOf(int arity)
    {
        var components = Enumerable.Range(1, arity)
            .Select(position => position % 2 == 1 ? (object)(position * 11L) : $"v{position}")
            .ToArray();
        var definition = typeof(Std.Tuple2<,>).Assembly.GetType($"Daml.Runtime.Stdlib.Tuple{arity}`{arity}")!;
        var closed = definition.MakeGenericType([.. components.Select(component => component.GetType())]);
        return Activator.CreateInstance(closed, components)!;
    }
}
