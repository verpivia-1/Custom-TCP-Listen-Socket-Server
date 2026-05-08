using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    internal class NetworkPlayerTransform : NetworkBehaviour
    {
        NetworkVariable<Vector3> Position;

        public NetworkPlayerTransform()
        {
            Position = RegisterVariable<Vector3>(Vector3.Zero);

        }
    }
}
