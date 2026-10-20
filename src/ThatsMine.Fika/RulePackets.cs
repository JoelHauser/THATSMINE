using Fika.Core.Networking.LiteNetLib.Utils;

namespace ThatsMine.Fika
{
    /// <summary>
    /// Client to host at raid start: "this game has THATSMINE". The host only ever sends to a
    /// client that said this, because Fika throws on a packet type nobody registered.
    /// </summary>
    public class HelloPacket : INetSerializable
    {
        /// <summary>Not used yet; there so a later version can tell versions apart.</summary>
        public int Protocol = 1;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Protocol);
        }

        public void Deserialize(NetDataReader reader)
        {
            Protocol = reader.GetInt();
        }
    }

    /// <summary>Host to client: the host's co-op loot rules, sent on hello and on every change.</summary>
    public class RulesPacket : INetSerializable
    {
        public bool OwnKillsOnly;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(OwnKillsOnly);
        }

        public void Deserialize(NetDataReader reader)
        {
            OwnKillsOnly = reader.GetBool();
        }
    }
}
