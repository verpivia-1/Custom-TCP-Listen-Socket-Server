using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Utils
{
    internal class IdGenerator
    {
        readonly HashSet<int> _idSet;
        readonly Queue<int> _availableIds; // 발급 가능한 id
        readonly object _gate = new();

        public IdGenerator(int max = 100)
        {
            _idSet = new HashSet<int>();
            _availableIds = new Queue<int>(max);

            for (int i = 1; i <= max; i++)
                _availableIds.Enqueue(i);
        }
        public int AssignId()
        {
            lock (_gate)
            {
                if (_availableIds.Count > 0)
                {
                    int id = _availableIds.Dequeue();
                    _idSet.Add(id);
                    return id;
                }
                else
                {
                    return -1;
                }
            }
        }
        public void ReleaseId(int id)
        {
            lock (_gate)
            {
                if (_idSet.Remove(id))
                {
                    _availableIds.Enqueue(id);
                }
                else
                {
                    throw new Exception($"{id} 는 발급한 적 없는 id 입니다...");
                }
            }
        }
    }
}