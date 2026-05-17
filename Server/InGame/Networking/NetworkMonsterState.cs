namespace Server.InGame.Networking
{
    internal class NetworkMonsterState : NetworkBehaviour
    {
        public NetworkVariable<int>   State { get; private set; }
        public NetworkVariable<float> Hp    { get; private set; }

        readonly float _baseHp;

        public NetworkMonsterState(float baseHp)
        {
            _baseHp = baseHp;
            State   = RegisterVariable<int>(0);
            Hp      = RegisterVariable<float>();
        }

        public override void OnNetworkSpawn()
        {
            Hp.Value = _baseHp;
        }
    }
}
