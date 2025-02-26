using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pode
{
    public class PodeListener : IDisposable
    {
        public IDictionary<string, PodeWebSocket> WebSockets { get; private set; }
        public bool IsListening { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool ErrorLoggingEnabled { get; set; }
        public string[] ErrorLoggingLevels { get; set; }
        public CancellationToken CancellationToken { get; private set; }

        private IList<PodeSocket> Sockets;

        private BlockingCollection<PodeContext> Contexts;
        public int ContextsCount
        {
            get => Contexts.Count;
        }

        private BlockingCollection<PodeServerSignal> ServerSignals;
        public int ServerSignalsCount
        {
            get => ServerSignals.Count;
        }

        private BlockingCollection<PodeClientSignal> ClientSignals;
        public int ClientSignalsCount
        {
            get => ClientSignals.Count;
        }

        private int _requestTimeout = 30;
        public int RequestTimeout
        {
            get => _requestTimeout;
            set
            {
                _requestTimeout = value <= 0 ? 30 : value;
            }
        }

        private int _requestBodySize = 104857600; // 100MB
        public int RequestBodySize
        {
            get => _requestBodySize;
            set
            {
                _requestBodySize = value <= 0 ? 104857600 : value;
            }
        }

        public PodeListener(CancellationToken cancellationToken = default(CancellationToken))
        {
            CancellationToken = cancellationToken == default(CancellationToken)
                ? cancellationToken
                : (new CancellationTokenSource()).Token;

            IsDisposed = false;

            Sockets = new List<PodeSocket>();
            WebSockets = new Dictionary<string, PodeWebSocket>();
            Contexts = new BlockingCollection<PodeContext>();
            ServerSignals = new BlockingCollection<PodeServerSignal>();
            ClientSignals = new BlockingCollection<PodeClientSignal>();
        }

        public void Add(PodeSocket socket)
        {
            var foundSocket = Sockets.FirstOrDefault(x => x.Equals(socket));
            if (foundSocket == default(PodeSocket))
            {
                Bind(socket);
            }
            else
            {
                foundSocket.Merge(socket);
            }
        }

        private void Bind(PodeSocket socket)
        {
            socket.BindListener(this);
            Sockets.Add(socket);
        }

        public PodeContext GetContext(CancellationToken cancellationToken = default(CancellationToken))
        {
            return (cancellationToken == default(CancellationToken)
                ? Contexts.Take()
                : Contexts.Take(cancellationToken));
        }

        public Task<PodeContext> GetContextAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return (cancellationToken == default(CancellationToken)
                ? Task.Factory.StartNew(() => GetContext())
                : Task.Factory.StartNew(() => GetContext(cancellationToken), cancellationToken));
        }

        public void AddContext(PodeContext context)
        {
            lock (Contexts)
            {
                Contexts.Add(context);
            }
        }

        public void AddWebSocket(PodeWebSocket webSocket)
        {
            lock (WebSockets)
            {
                if (WebSockets.ContainsKey(webSocket.ClientId))
                {
                    WebSockets[webSocket.ClientId] = webSocket;
                }
                else
                {
                    WebSockets.Add(webSocket.ClientId, webSocket);
                }
            }
        }

        public PodeServerSignal GetServerSignal(CancellationToken cancellationToken = default(CancellationToken))
        {
            return (cancellationToken == default(CancellationToken)
                ? ServerSignals.Take()
                : ServerSignals.Take(cancellationToken));
        }

        public Task<PodeServerSignal> GetServerSignalAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return (cancellationToken == default(CancellationToken)
                ? Task.Factory.StartNew(() => GetServerSignal())
                : Task.Factory.StartNew(() => GetServerSignal(cancellationToken), cancellationToken));
        }

        public void AddServerSignal(string value, string path, string clientId)
        {
            lock (ServerSignals)
            {
                ServerSignals.Add(new PodeServerSignal(value, path, clientId));
            }
        }

        public PodeClientSignal GetClientSignal(CancellationToken cancellationToken = default(CancellationToken))
        {
            return (cancellationToken == default(CancellationToken)
                ? ClientSignals.Take()
                : ClientSignals.Take(cancellationToken));
        }

        public Task<PodeClientSignal> GetClientSignalAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return (cancellationToken == default(CancellationToken)
                ? Task.Factory.StartNew(() => GetClientSignal())
                : Task.Factory.StartNew(() => GetClientSignal(cancellationToken), cancellationToken));
        }

        public void AddClientSignal(PodeClientSignal signal)
        {
            lock (ClientSignals)
            {
                ClientSignals.Add(signal);
            }
        }

        public void Start()
        {
            foreach (var socket in Sockets)
            {
                socket.Listen();
                socket.Start();
            }

            IsListening = true;
        }

        public void Dispose()
        {
            // stop listening
            IsListening = false;

            // shutdown the sockets
            for (var i = Sockets.Count - 1; i >= 0; i--)
            {
                Sockets[i].Dispose();
            }

            Sockets.Clear();

            // close existing contexts
            foreach (var _context in Contexts.ToArray())
            {
                _context.Dispose(true);
            }

            // close connected web sockets
            foreach (var _socket in WebSockets.Values)
            {
                _socket.Context.Dispose(true);
            }

            // disposed
            IsDisposed = true;
        }
    }
}