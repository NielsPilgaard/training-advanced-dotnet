using System.Text;
using EventStore.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Scheduling.EventSourcing;

namespace Scheduling.Infrastructure.EventStore;

public static class EsEventSerde
{
    public static object Deserialize(this ResolvedEvent resolvedEvent)
    {
        var dataToType = TypeMapper.GetDataToType(resolvedEvent.Event.EventType);
        var jsonData = Encoding.UTF8.GetString(resolvedEvent.Event.Data.ToArray());
        var data = JObject.Parse(jsonData);
        return dataToType(data);
    }

    public static EventData Serialize(this object @event, Uuid uuid, CommandMetadata metadata)
    {
        var typeToData = TypeMapper.GetTypeToData(@event.GetType());
        var (name, jObject) = typeToData(@event);
        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(jObject));

        return new EventData(
            uuid,
            name,
            data,
            Serialize(
                new EventMetadata(
                    @event.GetType().FullName!,
                    metadata.CorrelationId,
                    metadata.CausationId
                )
            )
        );
    }

    private static byte[] Serialize(this object data) =>
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

    public static T Deserialize<T>(this ResolvedEvent resolvedEvent)
    {
        var jsonData = Encoding.UTF8.GetString(resolvedEvent.Event.Data.ToArray());
        return JsonConvert.DeserializeObject<T>(jsonData)!;
    }

    public static EventMetadata DeserializeMetadata(this ResolvedEvent resolvedEvent)
    {
        var jsonData = Encoding.UTF8.GetString(resolvedEvent.Event.Metadata.ToArray());
        return JsonConvert.DeserializeObject<EventMetadata>(jsonData)!;
    }

    public static EventData SerializeSnapshot(this object snapshot, SnapshotMetadata metadata)
    {
        return new EventData(
            Uuid.NewUuid(),
            "doctor-day-snapshot",
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot)),
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata)));
    }

    public static SnapshotMetadata DeserializeSnapshotMetadata(this ResolvedEvent resolvedEvent)
    {
        var jsonData = Encoding.UTF8.GetString(resolvedEvent.Event.Metadata.ToArray());
        return JsonConvert.DeserializeObject<SnapshotMetadata>(jsonData)!;
    }

    public static (object command, CommandMetadata metadata) DeserializeCommand(this ResolvedEvent resolvedEvent)
    {
        var command = JsonConvert
            .DeserializeObject(Encoding.UTF8.GetString(resolvedEvent.Event.Data.ToArray()),
                new JsonSerializerSettings {TypeNameHandling = TypeNameHandling.Objects})!;

        var metadata = JsonConvert
            .DeserializeObject<CommandMetadata>(Encoding.UTF8.GetString(resolvedEvent.Event.Metadata.ToArray()))!;

        return (command, metadata);
    }

    public static EventData SerializeCommand(this object data, CommandMetadata metadata)
    {
        return new EventData(
            Uuid.NewUuid(),
            data.GetType().Name,
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data,
                new JsonSerializerSettings {TypeNameHandling = TypeNameHandling.Objects})),
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata)));
    }
}