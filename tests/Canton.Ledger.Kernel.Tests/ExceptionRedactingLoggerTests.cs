// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class ExceptionRedactingLoggerTests : IDisposable
{
    private readonly CapturingLoggerFactory _loggerFactory = new();
    private readonly ILogger _inner;

    public ExceptionRedactingLoggerTests() => _inner = _loggerFactory.CreateLogger("test");

    public void Dispose() => _loggerFactory.Dispose();

    private static void Emit(ILogger logger, LogLevel level, Exception? exception, string message) =>
        logger.Log(level, new EventId(0), message, exception, (text, _) => text);

    [Fact]
    public void Redacting_replaces_the_exception_by_its_type_name_and_keeps_the_message_and_level()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: false);

        Emit(logger, LogLevel.Warning, new InvalidOperationException("contract 00abc not found"), "decode failed");

        var record = _loggerFactory.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Be("decode failed");
        record.Exception!.Message.Should().Be("System.InvalidOperationException");
        record.Exception.ToString().Should().NotContain("00abc");
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void Redacting_keeps_the_stack_of_the_original_exception()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: false);
        var original = Thrown(new InvalidOperationException("contract 00abc not found"));

        Emit(logger, LogLevel.Error, original, "failed");

        var exception = _loggerFactory.Records.Should().ContainSingle().Subject.Exception!;
        exception.StackTrace.Should().Be(original.StackTrace).And.Contain(nameof(Thrown));
    }

    [Fact]
    public void Redacting_renders_the_original_type_and_stack_but_no_message_in_ToString()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: false);

        Emit(logger, LogLevel.Error, Thrown(new InvalidOperationException("contract 00abc not found")), "failed");

        var text = _loggerFactory.Records.Should().ContainSingle().Subject.Exception!.ToString();
        text.Should().StartWith("System.InvalidOperationException").And.Contain(nameof(Thrown));
        text.Should().NotContain("00abc");
    }

    [Fact]
    public void Redacting_renders_the_inner_exception_chain_as_type_and_stack_without_messages()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: false);
        var chain = Thrown(new InvalidOperationException("outer 00abc", Thrown(new ArgumentException("party alice::1220"))));

        Emit(logger, LogLevel.Error, chain, "failed");

        var text = _loggerFactory.Records.Should().ContainSingle().Subject.Exception!.ToString();
        text.Should().Contain("System.ArgumentException");
        text.Should().NotContain("alice::1220").And.NotContain("00abc");
    }

    [Fact]
    public void Redacting_leaves_a_record_without_exception_untouched()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: false);

        Emit(logger, LogLevel.Information, null, "plain");

        _loggerFactory.Records.Should().ContainSingle().Which.Exception.Should().BeNull();
    }

    [Fact]
    public void Emitting_returns_the_inner_logger_so_the_exception_reaches_the_sink_verbatim()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: true);
        var exception = new InvalidOperationException("contract 00abc not found");

        Emit(logger, LogLevel.Warning, exception, "decode failed");

        logger.Should().BeSameAs(_inner);
        _loggerFactory.Records.Should().ContainSingle().Which.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public void Redacting_twice_does_not_nest_a_second_decorator()
    {
        var once = _inner.RedactingExceptionsUnless(emit: false);

        once.RedactingExceptionsUnless(emit: false).Should().BeSameAs(once);
    }

    [Fact]
    public void Redacting_reports_the_levels_the_inner_logger_has_enabled()
    {
        var logger = _inner.RedactingExceptionsUnless(emit: false);

        logger.IsEnabled(LogLevel.Debug).Should().Be(_inner.IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void Redacting_an_exception_returns_a_wrapper_named_after_the_original_type()
    {
        var redacted = new InvalidOperationException("contract 00abc not found").RedactingUnless(emit: false);

        redacted.Message.Should().Be("System.InvalidOperationException");
        redacted.RedactingUnless(emit: false).Should().BeSameAs(redacted);
    }

    [Fact]
    public void Emitting_an_exception_returns_it_untouched()
    {
        var exception = new InvalidOperationException("contract 00abc not found");

        exception.RedactingUnless(emit: true).Should().BeSameAs(exception);
    }
}
