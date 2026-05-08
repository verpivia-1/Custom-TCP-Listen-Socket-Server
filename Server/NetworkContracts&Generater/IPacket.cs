using Server.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Server.NetworkContracts_Generater
{
    public interface IPacket
    {
        PacketId PacketId { get; }

        void Serialize(BinaryWriter writer);

        void Deserialize(BinaryReader reader);
    }
}
