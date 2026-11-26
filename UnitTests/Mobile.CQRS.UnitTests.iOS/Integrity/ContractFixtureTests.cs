using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Mobile.CQRS.Domain;
using Mobile.CQRS.Samples.OfflineSync;
using Mobile.CQRS.SQLite;
using Mobile.CQRS.SQLite.Domain;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Integrity;

[TestFixture]
public class ContractFixtureTests
{
    private static string Fixture(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts", "v1", name);

    [TestCase(false), TestCase(true)]
    public void StoresAndReplayMatchPortableStreamFixture(bool sqlite)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Fixture("stock-stream.json")));
        var root = fixture.RootElement;
        Assert.AreEqual(1, root.GetProperty("formatVersion").GetInt32());
        var id = root.GetProperty("aggregateId").GetGuid();
        var codec = new StockEventSerializer();
        var path = Path.Combine(Path.GetTempPath(), "contract-" + Guid.NewGuid() + ".db");
        using var connection = sqlite ? new SQLiteConnection(path) : null;
        IMergableEventStore store = sqlite ? new EventStore(connection, codec) : new InMemoryEventStore(codec);
        try
        {
            foreach (var step in root.GetProperty("steps").EnumerateArray())
            {
                var operation = step.GetProperty("operation").GetString();
                if (operation is "append" or "merge")
                {
                    var events = step.GetProperty("events").EnumerateArray()
                        .Select(evt => codec.DeserializeFromString(evt.GetRawText())).ToArray();
                    var version = step.GetProperty("expectedVersion").GetInt32();
                    void Apply()
                    {
                        if (operation == "append") store.SaveEvents(id, events, version);
                        else
                        {
                            using var scope = sqlite ? new SqlUnitOfWorkScope(connection) : null;
                            store.MergeEvents(id, events, version, step.GetProperty("afterVersion").GetInt32());
                            scope?.Commit();
                        }
                    }
                    switch (step.GetProperty("result").GetString())
                    {
                        case "ok": Apply(); break;
                        case "concurrency": Assert.Throws<ConcurrencyException>(() => Apply()); break;
                        case "invalid": Assert.Throws<InvalidOperationException>(() => Apply()); break;
                        default: Assert.Fail("Unknown fixture result"); break;
                    }
                }
                else
                {
                    var range = operation == "read-after" ? store.GetEventsAfterVersion(id, step.GetProperty("afterVersion").GetInt32())
                        : store.GetEventsUpToVersion(id, step.GetProperty("upToVersion").GetInt32());
                    CollectionAssert.AreEqual(step.GetProperty("versions").EnumerateArray().Select(x => x.GetInt32()), range.Select(x => x.Version));
                }
                var aggregate = new StockAggregate { Identity = id };
                aggregate.LoadFromEvents(store.GetAllEvents(id));
                Assert.AreEqual(step.GetProperty("quantity").GetInt32(), aggregate.Quantity, operation);
                Assert.AreEqual(step.GetProperty("version").GetInt32(), aggregate.Version, operation);
            }
        }
        finally { connection?.Dispose(); File.Delete(path); }
    }

    [Test]
    public async Task PublicOfflineSampleMatchesPortableRecoveryScenario()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Fixture("offline-recovery.json")));
        Assert.AreEqual(1, fixture.RootElement.GetProperty("formatVersion").GetInt32());
        var scenario = JsonSerializer.Deserialize<RecoveryScenario>(fixture.RootElement.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var directory = Path.Combine(Path.GetTempPath(), "contract-recovery-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var output = new StringWriter();
            await Demo.RunAsync(directory, output, scenario);
            StringAssert.Contains($"quantity {scenario.ExpectedQuantity} at version {scenario.ExpectedVersion}", output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task NativeReceiptMatchesFrozenCanonicalBatchAndFingerprint()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Fixture("sync-receipt.json")));
        var root = fixture.RootElement;
        Assert.AreEqual(1, root.GetProperty("formatVersion").GetInt32());
        var canonical = root.GetProperty("canonicalBatchJson").GetString();
        var batch = JsonSerializer.Deserialize<SyncBatch>(canonical);
        Assert.AreEqual(canonical, JsonSerializer.Serialize(batch));
        Assert.AreEqual(root.GetProperty("sha256").GetString(), SyncProtocol.Fingerprint(batch));
        Assert.AreEqual(SyncProtocol.Fingerprint(batch), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        var payload = new StockEventSerializer().DeserializeFromString(batch.Events.Single().Data);
        Assert.AreEqual(batch.Events.Single().Data, new StockEventSerializer().SerializeToString(payload));
        var directory = Path.Combine(Path.GetTempPath(), "contract-receipt-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            await using var context = Demo.Context(Path.Combine(directory, "canonical.db"));
            var endpoint = context.AsSyncRemote("contract-stock-canonical-v1");
            await endpoint.AppendAsync(batch);
            await endpoint.AppendAsync(batch);
            var mutated = (StockAdjusted)new StockEventSerializer().DeserializeFromString(batch.Events.Single().Data);
            mutated.Delta = 999;
            var changed = batch with { Events = SyncProtocol.Encode(new[] { mutated }, new StockEventSerializer()) };
            Assert.ThrowsAsync<ConflictException>(() => endpoint.AppendAsync(changed));
            CollectionAssert.AreEqual(batch.Events, (await endpoint.ReadAsync(batch.AggregateId)).Events);
            using var scope = context.BeginUnitOfWork();
            Assert.AreEqual(1, scope.GetRegisteredObject<SQLiteConnection>().ExecuteScalar<int>("select count(*) from SyncReceipt"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
