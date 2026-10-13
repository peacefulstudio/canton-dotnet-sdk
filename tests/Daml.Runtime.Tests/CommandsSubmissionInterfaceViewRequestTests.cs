// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandsSubmissionInterfaceViewRequestTests
{
    private static CommandsSubmission Submission() =>
        CommandsSubmission.Single(new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create()));

    [Fact]
    public void A_submission_requests_no_interface_views_by_default()
    {
        Submission().InterfaceViewRequests.Should().BeEmpty();
    }

    [Fact]
    public void WithInterfaceViewRequest_records_the_interface_marker_descriptor()
    {
        var requested = Submission().WithInterfaceViewRequest<Gadget>();

        requested.InterfaceViewRequests.Should().Equal(
            new DamlTypeDescriptor(
                new Identifier("gadget-pkg", "Acme.Gadget", "IGadget"), DamlTypeKind.Interface, "gadget-package"));
    }

    [Fact]
    public void WithInterfaceViewRequest_records_one_interface_once()
    {
        var requested = Submission().WithInterfaceViewRequest<Gadget>().WithInterfaceViewRequest<Gadget>();

        requested.InterfaceViewRequests.Should().HaveCount(1);
    }

    [Fact]
    public void WithInterfaceViewRequest_ignores_a_template_marker()
    {
        var submission = Submission();

        submission.WithInterfaceViewRequest<Widget>().Should().BeSameAs(submission);
    }

    [Fact]
    public void Requested_interface_views_take_part_in_submission_equality()
    {
        var plain = Submission();
        var requested = plain.WithInterfaceViewRequest<Gadget>();

        requested.Should().NotBe(plain);
        requested.Should().Be(plain.WithInterfaceViewRequest<Gadget>());
        requested.GetHashCode().Should().Be(plain.WithInterfaceViewRequest<Gadget>().GetHashCode());
        requested.GetHashCode().Should().NotBe(plain.GetHashCode());
    }

    private sealed record Gadget : IDamlInterface
    {
        public static Identifier InterfaceId { get; } = new("gadget-pkg", "Acme.Gadget", "IGadget");
        public static string PackageId => "gadget-pkg";
        public static string PackageName => "gadget-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record Widget : ITemplate
    {
        public static Identifier TemplateId { get; } = new("widget-pkg", "Acme.Widget", "Widget");
        public static string PackageId => "widget-pkg";
        public static string PackageName => "widget-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
