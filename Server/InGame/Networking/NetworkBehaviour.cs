using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    public abstract class NetworkBehaviour
    {
        public NetworkObject NetworkObject { get; internal set; }
        public int NetworkObjectId => NetworkObject.NetworkObjectId;
        public int OwnerClientId => NetworkObject.OwnerClientId;
        public IList<INetworkVariable> NetworkVariables => _networkVariables;

        readonly List<INetworkVariable> _networkVariables = new();
        public bool IsDirty
        {
            get
            {
                foreach (var networkVariable in _networkVariables)
                    if (networkVariable.IsDirty)
                        return true;

                return false;
            }
        }
        protected NetworkVariable<T> RegisterVariable<T>(T initialValue = default)
        {
            NetworkVariable<T> variable = new NetworkVariable<T>(initialValue);
            _networkVariables.Add(variable);
            return variable;
        }

        public void MarkClean()
        {
            foreach (var syncVariable in _networkVariables)
                syncVariable.MarkClean();
        }
        public virtual void OnNetworkSpawn() { }
        public virtual void OnNetworkDespawn() { }
    }
}
