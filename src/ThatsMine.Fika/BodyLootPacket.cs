using Fika.Core.Networking.LiteNetLib.Utils;

namespace ThatsMine.Fika
{
    /// <summary>
    /// Client to host: "I opened (or closed) the loot screen on this body". The looter is named
    /// by profile id rather than taken from the connection, so a close always matches its open.
    /// </summary>
    public class BodyLootPacket : INetSerializable
    {
        public bool Opened;
        public string LooterProfileId;
        public string BodyProfileId;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Opened);
            writer.Put(LooterProfileId ?? "");
            writer.Put(BodyProfileId ?? "");
        }

        public void Deserialize(NetDataReader reader)
        {
            Opened = reader.GetBool();
            LooterProfileId = reader.GetString();
            BodyProfileId = reader.GetString();
        }
    }
}
