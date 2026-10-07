// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.DefaultTarget;
using Daml.Codegen.Testing.Conformance.Disclosure;
using Daml.Codegen.Testing.Conformance.SubmitShapes;
using Daml.Runtime.Contracts;

namespace Daml.Codegen.Testing.Conformance.Tests;

internal static partial class GeneratedStjSampleTable
{
    private static void AddSubmitShapes(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Ephemeral)] = [new Ephemeral(Alice, "ephemeral-tag")];
        samples[typeof(EphemeralFactory)] = [new EphemeralFactory(Alice)];
        samples[typeof(EphemeralFactory.Churn)] = [new EphemeralFactory.Churn(new ContractId<Ephemeral>("00ee11"))];
        samples[typeof(Ticket)] = [new Ticket(Alice, Bob)];
        samples[typeof(TicketDesk)] = [new TicketDesk(Alice, Bob)];
        samples[typeof(TicketDesk.Issue)] = [new TicketDesk.Issue()];
        samples[typeof(TicketDesk.Pair)] = [new TicketDesk.Pair(true)];
        samples[typeof(TicketDesk.Reserve)] = [new TicketDesk.Reserve()];
        samples[typeof(TicketDesk.Retire)] = [new TicketDesk.Retire(new ContractId<Ticket>("00ff22"))];
    }

    private static void AddSingleTemplateFamilies(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Offer)] = [new Offer(Alice, 120)];
        samples[typeof(Offer.Inspect)] = [new Offer.Inspect(Bob)];
        samples[typeof(Note)] = [new Note(Alice, "note body", 3)];
    }
}
