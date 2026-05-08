using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    public interface INetworkVariable
    {
        bool IsDirty { get; }
        void MarkClean();
        void Serialize(BinaryWriter writer);
        void Deserialize(BinaryReader reader);
    }
}
