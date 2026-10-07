// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.KeyBuilders;
using Daml.Runtime.Stdlib;

namespace Daml.Codegen.Testing.Conformance.Tests;

internal static partial class GeneratedStjSampleTable
{
    private static void AddContractKeys(Dictionary<Type, object[]> samples)
    {
        samples[typeof(Account)] = [new Account(Alice, "savings", 1_000)];
        samples[typeof(Account.Credit)] = [new Account.Credit(250)];
        samples[typeof(Account.CurrentBalance)] = [new Account.CurrentBalance()];
        samples[typeof(AccountKey)] = [new AccountKey(Alice, "savings")];
        samples[typeof(Calendar)] = [new Calendar("holidays-2026", ["new-year", "midsummer"])];
        samples[typeof(DescribeCharter)] = [new DescribeCharter()];
        samples[typeof(Enrollment)] =
        [
            new Enrollment(Alice, "enrolled", true),
            new Enrollment(Bob, null, false),
        ];
        samples[typeof(Enrollment.Toggle)] = [new Enrollment.Toggle()];
        samples[typeof(Holiday)] =
        [
            new Holiday(Alice, new Calendar("holidays-2026", ["new-year", "midsummer"])),
        ];
        samples[typeof(Holiday.Extend)] = [new Holiday.Extend("harvest")];
        samples[typeof(HolidayKey)] = [new HolidayKey(Alice, "holidays-2026")];
        samples[typeof(Membership)] = [new Membership(Alice, "gold-club", 3)];
        samples[typeof(Membership.Renew)] = [new Membership.Renew(4)];
        samples[typeof(Registration)] =
        [
            new Registration(Alice, new Optional<Optional<string>>.Some(new Optional<string>.Some("tagged"))),
            new Registration(Bob, new Optional<Optional<string>>.Some(new Optional<string>.None())),
            new Registration(Alice, new Optional<Optional<string>>.None()),
        ];
        samples[typeof(Schedule)] = [new Schedule(new ScheduleView(Alice, "ref-1"))];
        samples[typeof(Schedule.Reschedule)] = [new Schedule.Reschedule("moved")];
        samples[typeof(Steward)] = [new Steward(Alice, "stewardship charter")];
        samples[typeof(Steward.Revise)] = [new Steward.Revise("revised charter")];
        samples[typeof(StewardshipView)] = [new StewardshipView("summary")];
        samples[typeof(ScheduleKey)] = [new ScheduleKey(Alice, "sched-1")];
        samples[typeof(ScheduleView)] = [new ScheduleView(Alice, "ref-1")];
    }
}
