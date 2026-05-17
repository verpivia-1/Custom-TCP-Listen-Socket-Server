using System;

namespace Server.InGame
{
    internal enum MonsterState { Idle, Chase, Attack, Dead }

    internal class MonsterFSM
    {
        public MonsterState State { get; private set; } = MonsterState.Idle;

        public event Action<MonsterState> OnStateChanged;
        public event Action OnAttack;

        readonly float _detectRange;
        readonly float _attackRange;
        readonly float _attackCooldown;
        float _cooldownTimer;

        public MonsterFSM(float detectRange, float attackRange, float attackCooldown)
        {
            _detectRange    = detectRange;
            _attackRange    = attackRange;
            _attackCooldown = attackCooldown;
        }

        public void Tick(float distToPlayer, float deltaTime)
        {
            if (State == MonsterState.Dead) return;

            switch (State)
            {
                case MonsterState.Idle:
                    if (distToPlayer <= _detectRange)
                        ChangeState(MonsterState.Chase);
                    break;

                case MonsterState.Chase:
                    if (distToPlayer <= _attackRange)
                        ChangeState(MonsterState.Attack);
                    else if (distToPlayer > _detectRange)
                        ChangeState(MonsterState.Idle);
                    break;

                case MonsterState.Attack:
                    if (distToPlayer > _attackRange)
                    {
                        ChangeState(MonsterState.Chase);
                        break;
                    }
                    _cooldownTimer -= deltaTime;
                    if (_cooldownTimer <= 0f)
                    {
                        _cooldownTimer = _attackCooldown;
                        OnAttack?.Invoke();
                    }
                    break;
            }
        }

        public void Kill()
        {
            if (State == MonsterState.Dead) return;
            ChangeState(MonsterState.Dead);
        }

        void ChangeState(MonsterState next)
        {
            State = next;
            OnStateChanged?.Invoke(next);
            if (next == MonsterState.Attack)
            {
                _cooldownTimer = _attackCooldown;
                OnAttack?.Invoke();
            }
        }
    }
}
