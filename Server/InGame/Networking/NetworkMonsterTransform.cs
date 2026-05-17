using System.Numerics;

namespace Server.InGame.Networking
{
    internal class NetworkMonsterTransform : NetworkBehaviour
    {
        public NetworkVariable<Vector3> Position { get; private set; }

        public NetworkMonsterTransform()
        {
            Position = RegisterVariable<Vector3>(Vector3.Zero);
        }
    }
}
