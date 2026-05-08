using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    internal class NetworkPlayerState : NetworkBehaviour
    {
        public NetworkVariable<float> Hp;
        public NetworkVariable<float> HpMax;
        public NetworkVariable<bool> IsDead;

        readonly float _baseHp;

        public NetworkPlayerState(float baseHp)
        {
            _baseHp = baseHp;
            Hp    = RegisterVariable<float>();
            HpMax = RegisterVariable<float>();
            IsDead = RegisterVariable<bool>(false);
        }

        public override void OnNetworkSpawn()
        {
            HpMax.Value = _baseHp;
            Hp.Value    = _baseHp;
        }
    }
}