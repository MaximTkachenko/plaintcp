using System;

namespace Mtc.PlainTcp.Core.Common
{
    public class Pool<T>
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _cleanup;
        private readonly T[] _pool;
        private volatile int _poolIndex;

        public Pool(int size, Func<T> factory, Action<T> cleanup = null)
        {
            _factory = factory;
            _cleanup = cleanup;

            _pool = new T[size];
            for (int i = 0; i < size; i++)
            {
                _pool[i] = _factory.Invoke();
                _cleanup?.Invoke(_pool[i]);
            }
        }

        public T Get()
        {
            if (_poolIndex == _pool.Length)
            {
                return _factory.Invoke();
            }

            lock (_pool)
            {
                if (_poolIndex == _pool.Length)
                {
                    return _factory.Invoke();
                }

                return _pool[_poolIndex++];
            }
        }

        public void Return(T item)
        {
            if (_poolIndex == 0)
            {
                (item as IDisposable)?.Dispose();
                return;
            }

            lock (_pool)
            {
                if (_poolIndex == 0)
                {
                    (item as IDisposable)?.Dispose();
                    return;
                }

                _cleanup?.Invoke(item);
                _pool[--_poolIndex] = item;
            }
        }
    }
}
