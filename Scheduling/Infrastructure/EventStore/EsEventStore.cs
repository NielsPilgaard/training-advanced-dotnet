using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Client;
using Scheduling.EventSourcing;

namespace Scheduling.Infrastructure.EventStore;

public class EsEventStore : IEventStore
{
    private readonly EventStoreClient _client;
    private readonly string _tenantPrefix;

    public EsEventStore(EventStoreClient client, string tenantPrefix)
    {
        _client = client;
        _tenantPrefix = $"[{tenantPrefix}]";
    }

    public Task AppendCommand(string streamId, object command, CommandMetadata metadata)
    {
        var preparedCommand = command.SerializeCommand(metadata);

        return _client.AppendToStreamAsync(_tenantPrefix + streamId, StreamState.Any,
            new List<EventData> {preparedCommand});
    }

    public Task AppendEvents(string streamName, long version, CommandMetadata metadata, params object[] events)
    {
        if (!events.Any())
        {
            return Task.CompletedTask;
        }

        var preparedEvents = events.Select(e => e.Serialize(
            Uuid.NewUuid(),
            metadata
        )).ToList();

        if (version == -1)
        {
            return _client.AppendToStreamAsync(_tenantPrefix + streamName, StreamState.NoStream, preparedEvents);
        }

        return _client.AppendToStreamAsync(_tenantPrefix + streamName, Convert.ToUInt64(version), preparedEvents);
    }

    public Task AppendEvents(string streamName, CommandMetadata metadata, params object[] events)
    {
        if (!events.Any())
        {
            return Task.CompletedTask;
        }

        var preparedEvents = events.Select(e => e.Serialize(
            Uuid.NewUuid(),
            metadata
        )).ToList();

        return _client.AppendToStreamAsync(_tenantPrefix + streamName, StreamState.Any, preparedEvents);
    }

    public async Task<IEnumerable<object>> LoadEvents(string stream, int? version = null)
    {
        EventStoreClient.ReadStreamResult response;

        if (version == null || version == -1)
        {
            response = _client
                .ReadStreamAsync(Direction.Forwards, _tenantPrefix + stream, StreamPosition.Start);
        }
        else
        {
            response = _client
                .ReadStreamAsync(Direction.Forwards, _tenantPrefix + stream, Convert.ToUInt64(version));
        }


        if (await response.ReadState == ReadState.StreamNotFound)
        {
            return new List<object>();
        }

        return await response
            .Select(e => e.Deserialize())
            .ToListAsync();
    }

    public async Task<ulong?> GetLastVersion(string streamName)
    {
        var response =
            _client.ReadStreamAsync(Direction.Backwards, _tenantPrefix + streamName, StreamPosition.End, 1);

        if (await response.ReadState == ReadState.StreamNotFound)
        {
            return null;
        }

        return (await response.FirstAsync()).Event.EventNumber;
    }

    public Task AppendSnapshot(string streamName, int aggregateVersion, object snapshot)
    {
        var snapshotEvent = snapshot.SerializeSnapshot(new SnapshotMetadata(aggregateVersion));

        return _client.AppendToStreamAsync($"{_tenantPrefix}snapshot-{streamName}", StreamState.Any,
            new List<EventData> {snapshotEvent});
    }

    public async Task<SnapshotEnvelope?> LoadSnapshot(string streamName)
    {
        var response = _client
            .ReadStreamAsync(Direction.Backwards, $"{_tenantPrefix}snapshot-{streamName}", StreamPosition.End, 1);

        await response.ReadState;
        if (await response.ReadState == ReadState.StreamNotFound)
        {
            return null;
        }

        var snapshot = await response.FirstAsync();

        return new SnapshotEnvelope(
            snapshot.Deserialize(),
            snapshot.DeserializeSnapshotMetadata()
        );
    }

    public Task TruncateStream(string streamName, ulong beforeVersion)
    {
        return _client.AppendToStreamAsync($"$${_tenantPrefix}{streamName}", StreamState.Any,
            new List<EventData>
            {
                new EventData(
                    Uuid.NewUuid(),
                    "$metadata",
                    Encoding.UTF8.GetBytes("{\"$tb\":" + beforeVersion + "}"))
            });
    }

    public async Task<IEnumerable<CommandEnvelope>> LoadCommands(string commandStream)
    {
        var response = _client
            .ReadStreamAsync(Direction.Forwards, _tenantPrefix + commandStream, StreamPosition.Start);

        if (await response.ReadState == ReadState.StreamNotFound)
        {
            return new List<CommandEnvelope>();
        }

        return await response
            .Select(e => e.DeserializeCommand())
            .Select(e => new CommandEnvelope(e.command, e.metadata))
            .ToListAsync();
    }
}