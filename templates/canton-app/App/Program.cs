using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using CantonApp.Contracts.HelloWorld;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

await using var services = new ServiceCollection().AddCantonLedger(configuration).BuildServiceProvider();
var admin = services.GetRequiredService<IAdminClient>();
var ledger = services.GetRequiredService<ICantonLedgerClient>();

var dar = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dar").Single();
await admin.UploadDarAsync(await File.ReadAllBytesAsync(dar));

var author = (await admin.AllocatePartyAsync($"author-{Guid.NewGuid():N}")).Party;
await admin.GrantUserRightsAsync(configuration["Canton:Ledger:UserId"]!, [new UserRight.ActAs(author)]);

var submitter = new SubmitterInfo(author);
var greetingId = await ledger.TryCreateAsync(new Greeting(author, "Hello from Canton"), submitter).OneOrThrowAsync();
Console.WriteLine($"Created {greetingId.Value}");

foreach (var greeting in await ledger.SnapshotAsync<Greeting>(submitter))
{
    Console.WriteLine($"{greeting.Data.Author} says: {greeting.Data.Message}");
}
