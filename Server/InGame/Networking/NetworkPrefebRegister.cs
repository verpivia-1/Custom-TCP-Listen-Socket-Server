using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    internal class NetworkPrefabRegistry
    {
        // PrefabId 에 따라 NetworkObject 에 붙일 Behaviour 들을 생성하기위한 공장
        readonly Dictionary<int, Func<NetworkBehaviour[]>> _factories = new();
        readonly Dictionary<int, string> _names = new();

        public void Register(int prefabIndex, string name, Func<NetworkBehaviour[]> factory)
        {
            _factories[prefabIndex] = factory;
            _names[prefabIndex] = name;
        }

        public bool TryGetFactory(int prefabIndex, out string name, out Func<NetworkBehaviour[]> factory)
        {
            if (!_factories.TryGetValue(prefabIndex, out factory))
            {
                name = null;
                return false;
            }

            name = _names[prefabIndex];
            return true;
        }
    }
}
