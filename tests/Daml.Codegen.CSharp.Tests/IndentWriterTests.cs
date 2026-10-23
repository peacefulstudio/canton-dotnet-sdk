// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class IndentWriterTests
{
    [Fact]
    public void TrimTrailingBlankLine_drops_the_blank_line_after_the_last_written_line()
    {
        var sb = new StringBuilder();
        var writer = new IndentWriter(sb);
        writer.AppendLine("int X { get; }");
        writer.AppendLine();

        writer.TrimTrailingBlankLine();

        sb.ToString().Should().Be("int X { get; }\n");
    }

    [Fact]
    public void TrimTrailingBlankLine_keeps_the_text_when_no_blank_line_ends_it()
    {
        var sb = new StringBuilder();
        var writer = new IndentWriter(sb);
        writer.AppendLine("int X { get; }");

        writer.TrimTrailingBlankLine();

        sb.ToString().Should().Be("int X { get; }\n");
    }

    [Fact]
    public void TrimTrailingBlankLine_leaves_an_empty_writer_empty()
    {
        var sb = new StringBuilder();

        new IndentWriter(sb).TrimTrailingBlankLine();

        sb.ToString().Should().Be("");
    }
}
