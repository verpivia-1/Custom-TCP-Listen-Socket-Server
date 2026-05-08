using Server.NetworkContracts_Generater;

namespace Server.InGame.Networking
{
    public class NetworkObject
    {
        public int NetworkObjectId { get; }
        public int OwnerClientId { get; set; }
        public int PrefabIndex { get; }
        public bool IsSpawned { get; set; }
        public string Name { get; set; }
        public IList<NetworkBehaviour> Behaviours => _behaviours;
        readonly List<NetworkBehaviour> _behaviours = new();

        public bool IsDirty
        {
            get
            {
                foreach (var behaviour in _behaviours)
                    if (behaviour.IsDirty)
                        return true;

                return false;
            }
        }
        public NetworkObject(string name, int networkObjectId, int prefabIndex, int ownerClientId = 0)
        {
            Name = name;
            NetworkObjectId = networkObjectId;
            PrefabIndex = prefabIndex;
            OwnerClientId = ownerClientId;
        }
        public T AddBehaviour<T>(T behaviour) where T : NetworkBehaviour
        {
            behaviour.NetworkObject = this;
            _behaviours.Add(behaviour);
            return behaviour;
        }

        public T GetBehaviour<T>() where T : NetworkBehaviour
        {
            foreach (var behaviour in _behaviours)
                if (behaviour is T typed) return typed;
            return null;
        }

        public void MarkClean()
        {
            foreach (var behaviour in _behaviours)
                behaviour.MarkClean();
        }

        internal void NotifySpawn()
        {
            foreach (var behaviour in _behaviours)
                behaviour.OnNetworkSpawn();
        }

        internal void NotifyDespawn()
        {
            foreach (var behaviour in _behaviours)
                behaviour.OnNetworkDespawn();
        }

        // 신규 접속자 동기화 및 S_ObjectSpawned 페이로드 구성용
        public SpawnedObjectInfo ToSpawnedObjectInfo()
        {
            var deltas = new List<NetworkVariableDelta>();
            for (int bi = 0; bi < _behaviours.Count; bi++)
            {
                var behaviour = _behaviours[bi];
                for (int vi = 0; vi < behaviour.NetworkVariables.Count; vi++)
                {
                    using var ms = new MemoryStream();
                    using var writer = new BinaryWriter(ms);
                    behaviour.NetworkVariables[vi].Serialize(writer);
                    deltas.Add(new NetworkVariableDelta
                    {
                        ObjectId = NetworkObjectId,
                        BehaviourIndex = bi,
                        VariableIndex = vi,
                        Payload = ms.ToArray()
                    });
                }
            }
            return new SpawnedObjectInfo
            {
                ObjectId = NetworkObjectId,
                OwnerClientId = OwnerClientId,
                PrefabIndex = PrefabIndex,
                ObjectName = Name,
                VariableDeltaList = deltas
            };
        }
    }
}
