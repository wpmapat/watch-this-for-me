#:package Microsoft.Azure.Cosmos@3.54.0
#:package Newtonsoft.Json@13.0.3
// Deletes a throwaway scenario/load-test database from the LOCAL Cosmos emulator.
// Usage (repo root): dotnet run tests/scenarios/drop_database.cs <DatabaseName>
// Refuses to touch the real "WatchThisForMe" database.
using Microsoft.Azure.Cosmos;

if (args.Length != 1 || args[0] == "WatchThisForMe")
{
    Console.Error.WriteLine("Usage: dotnet run drop_database.cs <ThrowawayDatabaseName>  (never the real WatchThisForMe db)");
    return 1;
}

// The emulator's well-known public key - the same one in appsettings.Development.json.
var options = new CosmosClientOptions
{
    HttpClientFactory = () => new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }),
    ConnectionMode = ConnectionMode.Gateway
};
using var client = new CosmosClient(
    "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
    options);

await client.GetDatabase(args[0]).DeleteAsync();
Console.WriteLine($"Deleted database {args[0]}");
return 0;
