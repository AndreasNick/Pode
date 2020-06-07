using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Pode
{
    public class PodeSocket : IDisposable
    {
        public IPAddress IPAddress { get; private set; }
        public string Hostname { get; set; }
        public int Port { get; private set; }
        public IPEndPoint Endpoint { get; private set; }
        public X509Certificate Certificate { get; private set; }
        public SslProtocols Protocols { get; private set; }
        public Socket Socket { get; private set; }

        private ConcurrentQueue<SocketAsyncEventArgs> AcceptConnections;
        private ConcurrentQueue<SocketAsyncEventArgs> ReceiveConnections;

        private PodeListener Listener;

        public bool IsSsl
        {
            get => (Certificate != default(X509Certificate));
        }

        public int ReceiveTimeout
        {
            get => Socket.ReceiveTimeout;
            set => Socket.ReceiveTimeout = value;
        }

        public PodeSocket(IPAddress ipAddress, int port, SslProtocols protocols, X509Certificate certificate = null)
        {
            IPAddress = ipAddress;
            Port = port;
            Certificate = certificate;
            Protocols = protocols;
            Endpoint = new IPEndPoint(ipAddress, port);

            AcceptConnections = new ConcurrentQueue<SocketAsyncEventArgs>();
            ReceiveConnections = new ConcurrentQueue<SocketAsyncEventArgs>();

            Socket = new Socket(Endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, false);
            Socket.ReceiveTimeout = 100;
        }

        public void BindListener(PodeListener listener)
        {
            Listener = listener;
        }

        public void Listen()
        {
            Socket.Bind(Endpoint);
            Socket.Listen(int.MaxValue);
        }

        public void Start()
        {
            var args = default(SocketAsyncEventArgs);
            if (!AcceptConnections.TryDequeue(out args))
            {
                args = NewAcceptConnection();
            }

            args.AcceptSocket = default(Socket);
            args.UserToken = this;
            var raised = false;

            try
            {
                raised = ((PodeSocket)args.UserToken).Socket.AcceptAsync(args);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!raised)
            {
                ProcessAccept(args);
            }
        }

        private void StartReceive(SocketAsyncEventArgs acceptedEvent)
        {
            var args = default(SocketAsyncEventArgs);
            if (!ReceiveConnections.TryDequeue(out args))
            {
                args = NewReceiveConnection();
            }

            args.AcceptSocket = acceptedEvent.AcceptSocket;
            args.SetBuffer(new byte[0], 0, 0);
            args.UserToken = this;
            var raised = false;

            try
            {
                raised = args.AcceptSocket.ReceiveAsync(args);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (Listener.ErrorLoggingEnabled)
                {
                    PodeHelpers.WriteException(ex, Listener);
                }
                throw;
            }

            if (!raised)
            {
                ProcessReceive(args);
            }
        }

        private void ProcessAccept(SocketAsyncEventArgs args)
        {
            // get details
            var accepted = args.AcceptSocket;
            var socket = (PodeSocket)args.UserToken;
            var error = args.SocketError;

            // start the socket again
            socket.Start();

            // close socket if not successful
            if ((accepted == default(Socket)) || (error != SocketError.Success))
            {
                // close socket
                if (accepted != default(Socket))
                {
                    accepted.Close();
                }

                // add args back to connections
                ClearSocketAsyncEvent(args);
                AcceptConnections.Enqueue(args);
                return;
            }

            // start receive
            StartReceive(args);

            // add args back to connections
            ClearSocketAsyncEvent(args);
            AcceptConnections.Enqueue(args);
        }

        private void ProcessReceive(SocketAsyncEventArgs args)
        {
            // get details
            var received = args.AcceptSocket;
            var socket = (PodeSocket)args.UserToken;
            var error = args.SocketError;

            // close socket if not successful
            if ((received == default(Socket)) || (error != SocketError.Success))
            {
                // close socket
                if (received != default(Socket))
                {
                    received.Close();
                }

                // add args back to connections
                ClearSocketAsyncEvent(args);
                ReceiveConnections.Enqueue(args);
                return;
            }

            try
            {
                // create the request
                var request = new PodeRequest(received, socket.Certificate, socket.Protocols);

                // if we need to exit now, dispose and exit
                if (request.CloseImmediately || string.IsNullOrWhiteSpace(request.HttpMethod))
                {
                    PodeHelpers.WriteException(request.Error, Listener);
                    request.Dispose();
                    return;
                }

                // if websocket, and httpmethod != GET, close!
                if (request.IsWebSocket && !request.HttpMethod.Equals("GET", StringComparison.InvariantCultureIgnoreCase))
                {
                    request.Dispose();
                    return;
                }

                // add a new context
                socket.Listener.AddContext(request, new PodeResponse());
            }
            catch (Exception ex)
            {
                PodeHelpers.WriteException(ex, Listener);
            }

            // add args back to connections
            ClearSocketAsyncEvent(args);
            ReceiveConnections.Enqueue(args);
        }

        private SocketAsyncEventArgs NewAcceptConnection()
        {
            lock (AcceptConnections)
            {
                var args = new SocketAsyncEventArgs();
                args.Completed += new EventHandler<SocketAsyncEventArgs>(Accept_Completed);
                return args;
            }
        }

        private SocketAsyncEventArgs NewReceiveConnection()
        {
            lock (ReceiveConnections)
            {
                var args = new SocketAsyncEventArgs();
                args.Completed += new EventHandler<SocketAsyncEventArgs>(Receive_Completed);
                return args;
            }
        }

        private void Accept_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void Receive_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }

        public void Dispose()
        {
            CloseSocket(Socket);
        }

        public static void CloseSocket(Socket socket)
        {
            if (socket.Connected)
            {
                socket.Shutdown(SocketShutdown.Both);
            }

            socket.Close();
            socket.Dispose();
        }

        private void ClearSocketAsyncEvent(SocketAsyncEventArgs e)
        {
            e.AcceptSocket = default(Socket);
            e.UserToken = default(object);
        }
    }
}