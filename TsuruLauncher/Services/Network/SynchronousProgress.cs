using System;

namespace TsuruLauncher.Services.Network
{
    /// <summary>
    /// A lightweight implementation of IProgress&lt;T&gt; that invokes the callback directly 
    /// on the caller's thread, avoiding SynchronizationContext marshalling.
    /// Useful for high-frequency updates where the callback is already thread-safe.
    /// </summary>
    public class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}