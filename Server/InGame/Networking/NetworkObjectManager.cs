using Server.NetworkContracts_Generater;
using Server.Utils;

namespace Server.InGame.Networking
{
    internal class NetworkObjectManager
    {
        public IDictionary<int, NetworkObject> SpawnedObjects => _objects;
        readonly Dictionary<int, NetworkObject> _objects = new();

        readonly IdGenerator _idGenerator = new(1000);
        readonly NetworkPrefabRegistry _prefabRegistry;
        readonly Action<IPacket> _broadcast;
        readonly Action<int, IPacket> _broadcastExcept; // (excludeClientId, packet)

        public NetworkObjectManager(
            NetworkPrefabRegistry prefabRegistry,
            Action<IPacket> broadcast,
            Action<int, IPacket> broadcastExcept)
        {
            _prefabRegistry   = prefabRegistry;
            _broadcast        = broadcast;
            _broadcastExcept  = broadcastExcept;
        }

        public NetworkObject Spawn(int clientId, int prefabIndex)
        {
            if (!_prefabRegistry.TryGetFactory(prefabIndex, out string prefabName, out var factory))
                return null;

            int networkObjectId = _idGenerator.AssignId();
            if (networkObjectId <= 0)
                return null;

            var obj = new NetworkObject(prefabName, networkObjectId, prefabIndex, clientId);
            foreach (var behaviour in factory.Invoke())
                obj.AddBehaviour(behaviour);

            _objects[obj.NetworkObjectId] = obj;
            obj.IsSpawned = true;
            obj.NotifySpawn();

            _broadcast(new S_ObjectSpawned { ObjectInfo = obj.ToSpawnedObjectInfo() });
            return obj;
        }

        public bool Despawn(int networkObjectId)
        {
            if (!_objects.TryGetValue(networkObjectId, out var obj))
                return false;

            obj.IsSpawned = false;
            obj.NotifyDespawn();
            _objects.Remove(networkObjectId);
            _idGenerator.ReleaseId(networkObjectId);

            _broadcast(new S_ObjectDespawned { ObjectId = networkObjectId });
            return true;
        }

        // 30ms 루프에서 호출 — 서버가 직접 변경한 변수(HP 감소 등)를 브로드캐스트
        public void FlushDirtyObjects()
        {
            foreach (var obj in _objects.Values)
            {
                if (!obj.IsDirty) continue;

                for (int bi = 0; bi < obj.Behaviours.Count; bi++)
                {
                    var behaviour = obj.Behaviours[bi];
                    for (int vi = 0; vi < behaviour.NetworkVariables.Count; vi++)
                    {
                        var variable = behaviour.NetworkVariables[vi];
                        if (!variable.IsDirty) continue;

                        using var ms = new MemoryStream();
                        using var writer = new BinaryWriter(ms);
                        variable.Serialize(writer);

                        _broadcast(new S_NetworkVarUpdate
                        {
                            Delta = new NetworkVariableDelta
                            {
                                ObjectId       = obj.NetworkObjectId,
                                BehaviourIndex = bi,
                                VariableIndex  = vi,
                                Payload        = ms.ToArray()
                            }
                        });
                    }
                }

                obj.MarkClean();
            }
        }

        // C_NetworkVarUpdate 수신 시 호출 — 소유 클라이언트가 보낸 변수 갱신을 다른 클라이언트에 중계
        public void ApplyVariableUpdate(int senderClientId, NetworkVariableDelta delta)
        {
            if (!_objects.TryGetValue(delta.ObjectId, out var obj)) return;
            if (obj.OwnerClientId != senderClientId) return;

            int bi = delta.BehaviourIndex;
            int vi = delta.VariableIndex;
            if (bi < 0 || bi >= obj.Behaviours.Count) return;

            var behaviour = obj.Behaviours[bi];
            if (vi < 0 || vi >= behaviour.NetworkVariables.Count) return;

            var variable = behaviour.NetworkVariables[vi];

            // SetWithoutNotify 효과: Deserialize 후 즉시 MarkClean → flush 루프가 재브로드캐스트 안 함
            using var ms = new MemoryStream(delta.Payload);
            using var reader = new BinaryReader(ms);
            variable.Deserialize(reader);
            variable.MarkClean();

            // 보낸 클라이언트를 제외한 나머지에 중계
            _broadcastExcept(senderClientId, new S_NetworkVarUpdate { Delta = delta });
        }
    }
}
