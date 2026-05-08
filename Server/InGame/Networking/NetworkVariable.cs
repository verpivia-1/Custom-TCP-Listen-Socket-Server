using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    public class NetworkVariable<T> : INetworkVariable
    {
        bool _isDirty;
        T _value;
        public event Action<T, T> OnValueChanged; // 동기화 시키기(서버기준)

        public bool IsDirty => _isDirty; // 틱 기반 동기화. server 기준
        public T Value
        {
            get => _value;
            set
            {
                if (!EqualityComparer<T>.Default.Equals(_value, value))
                {
                    T old = _value;
                    _value = value;
                    _isDirty = true;
                    OnValueChanged?.Invoke(old, _value);
                }
            }
        }
        public NetworkVariable(T initialValue = default) // 기본값 지정.
        {
            _value = initialValue;
        }

        public void MarkClean() // 더티셋이 처리 되면 다시 처음상태로
        {
            _isDirty = false;
        }
        public void SetWithoutNotify(T value) 
        {
            _value = value;
        }

        public static implicit operator T(NetworkVariable<T> v) => v._value;
        public override string ToString() => _value?.ToString() ?? "null";

        public void Serialize(BinaryWriter writer) => NetworkVariableSerializer<T>.Write(writer, _value);
        public void Deserialize(BinaryReader reader) => Value = NetworkVariableSerializer<T>.Read(reader);
    }
}