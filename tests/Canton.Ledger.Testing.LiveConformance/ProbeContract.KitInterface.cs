// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;

namespace Conformance.Probe;

public sealed partial record ProbeContract : IImplements<global::Daml.Ledger.Abstractions.Testing.Conformance.IConformanceProbe>;
