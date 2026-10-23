// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Daml.Runtime.Serialization;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestLedgerClientConformanceTests : LedgerClientConformanceTests<RestConformanceProbe>
{
    protected override SubmitterInfo Reader { get; } = new Party("party::alice");

    protected override ILedgerClient CreateClient() =>
        new RestLedgerClient(new ParticipantHttpClientFactory(new ConformanceParticipantHandler()));

    protected override CommandIdConformanceFixture? CreateCommandIdFixture()
    {
        var participant = new RecordingSubmissionHandler();
        var client = new RestLedgerClient(new ParticipantHttpClientFactory(participant));

        return new CommandIdConformanceFixture(
            client,
            (writer, commandId) => writer.TryExerciseAsync<DamlUnit>(ArchiveProbe, Reader, commandId: commandId),
            (writer, commandId) => writer.TryCreateAsync(new RestConformanceProbe((Party)"party::alice"), Reader, commandId: commandId),
            () => ValueTask.FromResult(participant.RecordedCommandId));
    }

    protected override WriteConformanceFixture? CreateWriteFixture()
    {
        var authorized = new Party("party::alice");
        var client = new RestLedgerClient(new ParticipantHttpClientFactory(new ActAsAuthorizingSubmissionHandler(authorized)));

        return new WriteConformanceFixture(
            client,
            CommandsSubmission.Single(ArchiveProbe),
            authorized,
            new Party("party::mallory"));
    }

    private static readonly ExerciseCommand ArchiveProbe = new(
        RestConformanceProbe.TemplateId,
        new ContractId<RestConformanceProbe>("00probe"),
        new ChoiceName("Archive"),
        DamlUnit.Instance);

    private sealed class ParticipantHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:7575") };
    }
}

/// <summary>
/// The participant half of the command-id conformance scenario: it answers every submission with
/// one transaction carrying both a created probe and a consuming <c>Archive</c> exercise, and keeps
/// the <c>commandId</c> the request carried so the kit can read back what actually reached the
/// wire.
/// </summary>
internal sealed class RecordingSubmissionHandler : HttpMessageHandler
{
    private const string ProbeTemplateIdJson =
        """{"packageId": "conformance-pkg", "moduleName": "Conformance.Probe", "entityName": "Probe"}""";

    internal static readonly string SubmittedTransaction =
        $$$"""
        {
          "transaction": {
            "updateId": "upd-submitted",
            "offset": "1",
            "events": [
              {
                "CreatedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "00probe",
                  "packageName": "conformance-package",
                  "templateId": {{{ProbeTemplateIdJson}}},
                  "createArgument": {"owner": "party::alice"},
                  "witnessParties": ["party::alice"]
                }
              },
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "00probe",
                  "packageName": "conformance-package",
                  "templateId": {{{ProbeTemplateIdJson}}},
                  "choice": "Archive",
                  "choiceArgument": {},
                  "actingParties": ["party::alice"],
                  "consuming": true,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": {}
                }
              }
            ]
          }
        }
        """;

    /// <summary>The <c>commandId</c> the most recent submission carried, or <c>null</c> before any.</summary>
    public string? RecordedCommandId { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(body);
        RecordedCommandId = document.RootElement.TryGetProperty("commands", out var commands)
            && commands.TryGetProperty("commandId", out var commandId)
            ? commandId.GetString()
            : throw new InvalidOperationException(
                $"The submission carried no commandId, so the scenario cannot record one: {body}");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(SubmittedTransaction, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// The participant half of the submitter-authority conformance scenario: it accepts a submission
/// only when its <c>actAs</c> is exactly the authorized party and answers anything else with
/// <c>403 Forbidden</c>, so a client that merges or ignores the submitter parameter is rejected.
/// </summary>
internal sealed class ActAsAuthorizingSubmissionHandler(Party authorized) : HttpMessageHandler
{
    private const string SubmitAndWaitResponse = """{"updateId": "upd-submitted", "completionOffset": "1"}""";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var returnsTransaction = request.RequestUri!.AbsolutePath.EndsWith(
            "submit-and-wait-for-transaction", StringComparison.Ordinal);
        var commands = returnsTransaction ? document.RootElement.GetProperty("commands") : document.RootElement;
        var actAs = commands.GetProperty("actAs")
            .EnumerateArray().Select(party => party.GetString()).ToList();

        if (!actAs.SequenceEqual([authorized.Value]))
        {
            return Respond(request, HttpStatusCode.Forbidden, JsonSerializer.Serialize(new
            {
                code = "PERMISSION_DENIED",
                cause = $"submission acting as [{string.Join(",", actAs)}] is not authorized by {authorized.Value}",
                grpcCodeValue = 7,
                errorCategory = 1,
                context = new { },
            }));
        }

        return Respond(
            request,
            HttpStatusCode.OK,
            returnsTransaction
                ? RecordingSubmissionHandler.SubmittedTransaction
                : SubmitAndWaitResponse);
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, HttpStatusCode status, string json) =>
        new(status)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}

/// <summary>The Daml marker the conformance scenario's snapshot and streams are filtered to.</summary>
/// <param name="Owner">The party the probe contract is issued to.</param>
public sealed record RestConformanceProbe([property: DamlFieldAttribute("owner")] Party Owner)
    : ITemplate, IDamlRecord<RestConformanceProbe>, IHasChoices<RestConformanceProbe>, IImplements<IConformanceProbe>
{
    [ModuleInitializer]
    internal static void RegisterHandWritten()
    {
        GeneratedTypeReaders.ForRecord<RestConformanceProbe>();
        GeneratedTypeReaders.ForChoices<RestConformanceProbe>();
    }

    /// <inheritdoc cref="ITemplate" />
    public static RuntimeIdentifier TemplateId { get; } = new("conformance-pkg", "Conformance.Probe", "Probe");

    /// <inheritdoc cref="ITemplate" />
    public static string PackageId => "conformance-pkg";

    /// <inheritdoc cref="ITemplate" />
    public static string PackageName => "conformance-package";

    /// <inheritdoc cref="ITemplate" />
    public static Version PackageVersion { get; } = new(0, 1, 0);

    /// <inheritdoc cref="ITemplate" />
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    /// <inheritdoc cref="ITemplate" />
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        TestRecordReader.Read(
            json,
            context,
            ("owner", DamlLfJsonDecoders.ReadParty));
    /// <summary>Creates a probe from the wire record a created event carries.</summary>
    /// <returns>The probe the record decodes to.</returns>
    public static RestConformanceProbe FromRecord(DamlRecord record) =>
        new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

    /// <summary>The consuming Archive choice the conformance scenario exercises on the probe.</summary>
    public static Choice<RestConformanceProbe, DamlUnit, DamlUnit> ChoiceArchive { get; } = new()
    {
        Name = new ChoiceName("Archive"),
        Consuming = true,
        ArgumentEncoder = unit => unit,
        ResultDecoder = result => result.As<DamlUnit>(),
        ArgumentDecoder = value => value.As<DamlUnit>(),
        ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
        ResultJsonReader = DamlLfJsonDecoders.ReadUnit,
    };

    public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceArchive];
}

/// <summary>
/// The participant half of the JSON Ledger API conformance scenario: it serves the seeded ledger
/// end, filters the active-contract snapshot by the requested <c>activeAtOffset</c>, and filters
/// the update stream by the requested <c>(beginExclusive, endInclusive]</c> window and transaction
/// shape, and holds an open-ended read that has delivered everything seeded until the caller cancels
/// it, so the conformance checks exercise what <see cref="RestLedgerClient"/> asks for as well
/// as how it projects the answer.
/// </summary>
internal sealed class ConformanceParticipantHandler : HttpMessageHandler
{
    private const string LedgerEndPath = "/v2/state/ledger-end";
    private const string ActiveContractsPagePath = "/v2/state/active-contracts-page";
    private const string UpdatesPath = "/v2/updates";
    private const string LedgerEffectsShape = "TRANSACTION_SHAPE_LEDGER_EFFECTS";
    private const string AcsDeltaShape = "TRANSACTION_SHAPE_ACS_DELTA";
    private const long CreatedOffset = 1L;
    private const long UnclassifiableOffset = 2L;
    private const long ConsumedOffset = 3L;
    private const long LedgerEndOffset = 5L;
    private const string Synchronizer = "sync-1";

    private const string ProbePackageName = "conformance-package";

    private static readonly object ProbeTemplateId =
        new { packageId = "conformance-pkg", moduleName = "Conformance.Probe", entityName = "Probe" };

    private static readonly object ForeignTemplateId =
        new { packageId = "conformance-pkg", moduleName = "Conformance.Other", entityName = "Other" };

    private static readonly string[] Witnesses = ["party::alice"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        object payload = request.RequestUri!.AbsolutePath switch
        {
            LedgerEndPath => new { offset = LedgerEndOffset },
            ActiveContractsPagePath => new { activeContracts = ActiveContractsAt(OffsetField(body, "activeAtOffset")) },
            UpdatesPath => await UpdatesFor(body, cancellationToken),
            var unexpected => throw new InvalidOperationException(
                $"The conformance scenario seeds no response for '{unexpected}'."),
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private static object[] ActiveContractsAt(long activeAtOffset) =>
        [
            .. new[]
            {
                (Offset: CreatedOffset, Row: ActiveContract("00probe", ProbeTemplateId, CreatedOffset, ProbeInterfaceViews())),
                (Offset: UnclassifiableOffset, Row: ActiveContract("00foreign", ForeignTemplateId, UnclassifiableOffset, [])),
            }
            .Where(seeded => seeded.Offset <= activeAtOffset)
            .Select(seeded => seeded.Row),
        ];

    private static async Task<object[]> UpdatesFor(string body, CancellationToken cancellationToken)
    {
        var beginExclusive = OffsetField(body, "beginExclusive");
        var endInclusive = OptionalOffsetField(body, "endInclusive");
        var consumption = RequestsLedgerEffects(body) ? ConsumingExercise() : Archival();

        object[] window =
        [
            .. new[] { (Offset: CreatedOffset, Event: Creation()), (Offset: ConsumedOffset, Event: consumption) }
                .Where(seeded => seeded.Offset > beginExclusive && seeded.Offset <= (endInclusive ?? long.MaxValue))
                .Select(seeded => Transaction(seeded.Offset, seeded.Event)),
        ];

        if (endInclusive is null && window.Length == 0)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return window;
    }

    private static bool RequestsLedgerEffects(string body)
    {
        if (body.Contains(LedgerEffectsShape, StringComparison.Ordinal))
        {
            return true;
        }

        if (body.Contains(AcsDeltaShape, StringComparison.Ordinal))
        {
            return false;
        }

        throw new InvalidOperationException(
            $"The update request names neither '{AcsDeltaShape}' nor '{LedgerEffectsShape}': {body}");
    }

    private static long OffsetField(string body, string name) =>
        OptionalOffsetField(body, name)
            ?? throw new InvalidOperationException(
                $"The request omits the offset field '{name}', so the scenario cannot bound it: {body}");

    private static long? OptionalOffsetField(string body, string name)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty(name, out var offset)
            ? long.Parse(offset.GetString()!, CultureInfo.InvariantCulture)
            : null;
    }

    private static object ActiveContract(string contractId, object templateId, long offset, object[] interfaceViews) => new
    {
        contractEntry = new
        {
            JsActiveContract = new
            {
                createdEvent = CreatedEvent(contractId, templateId, offset, interfaceViews),
                synchronizerId = Synchronizer,
            },
        },
    };

    private static object ProbeInterfaceId() => new
    {
        packageId = IConformanceProbe.InterfaceId.PackageId,
        moduleName = IConformanceProbe.InterfaceId.ModuleName,
        entityName = IConformanceProbe.InterfaceId.EntityName,
    };

    private static object[] ProbeInterfaceViews() =>
    [
        new
        {
            interfaceId = ProbeInterfaceId(),
            viewStatus = new { code = 0, message = string.Empty },
            viewValue = new { amount = "42.5" },
        },
    ];

    private static object Transaction(long offset, object seeded) => new
    {
        update = new
        {
            Transaction = new
            {
                value = new
                {
                    offset = offset.ToString(CultureInfo.InvariantCulture),
                    synchronizerId = Synchronizer,
                    events = new[] { seeded },
                },
            },
        },
    };

    private static object CreatedEvent(string contractId, object templateId, long offset, object[] interfaceViews) => new
    {
        offset = offset.ToString(CultureInfo.InvariantCulture),
        contractId,
        packageName = ProbePackageName,
        templateId,
        createArgument = new { owner = "party::alice" },
        interfaceViews,
        witnessParties = Witnesses,
    };

    private static object Creation() =>
        new { CreatedEvent = CreatedEvent("00probe", ProbeTemplateId, CreatedOffset, ProbeInterfaceViews()) };

    private static object Archival() => new
    {
        ArchivedEvent = new
        {
            offset = ConsumedOffset.ToString(CultureInfo.InvariantCulture),
            contractId = "00probe",
            packageName = ProbePackageName,
            templateId = ProbeTemplateId,
            implementedInterfaces = new[] { ProbeInterfaceId() },
            witnessParties = Witnesses,
        },
    };

    private static object ConsumingExercise() => new
    {
        ExercisedEvent = new
        {
            offset = ConsumedOffset.ToString(CultureInfo.InvariantCulture),
            contractId = "00probe",
            packageName = ProbePackageName,
            templateId = ProbeTemplateId,
            choice = "Archive",
            choiceArgument = new { },
            actingParties = Witnesses,
            consuming = true,
            implementedInterfaces = new[] { ProbeInterfaceId() },
            witnessParties = Witnesses,
            exerciseResult = new { },
        },
    };
}
