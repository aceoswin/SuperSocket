using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Buffers;
using SuperSocket;
using SuperSocket.Channel;
using SuperSocket.ProtoBase;
using System.Threading;
using System.Net;

namespace SuperSocket.Server
{
    public class TcpSocketListener : IListener
    {
        private Socket _listenSocket;

        private CancellationTokenSource _cancellationTokenSource;
        private TaskCompletionSource<bool> _stopTaskCompletionSource;
        private Func<Socket, IChannel> _channelFactory;
        public ListenOptions Options { get; private set; }

        public TcpSocketListener(ListenOptions options, Func<Socket, IChannel> channelFactory)
        {
            Options = options;
            _channelFactory = channelFactory;
        }

        private IPEndPoint GetListenEndPoint(string ip, int port)
        {
            var ipAddress = IPAddress.None;

            if ("any".Equals(ip, StringComparison.OrdinalIgnoreCase))
            {
                ipAddress = IPAddress.Any;
            }
            else if ("IpV6Any".Equals(ip, StringComparison.OrdinalIgnoreCase))
            {
                ipAddress = IPAddress.IPv6Any;
            }
            else
            {
                ipAddress = IPAddress.Parse(ip);
            }

            return new IPEndPoint(ipAddress, port);
        }

        public bool IsRunning { get; private set; }

        public void Start()
        {
            var options = Options;

            try
            {
                var listenEndpoint = GetListenEndPoint(options.Ip, options.Port);
                var listenSocket = _listenSocket = new Socket(listenEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                
                listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                listenSocket.LingerState = new LingerOption(false, 0);

                if (options.NoDelay)
                    listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);
                
                listenSocket.Bind(listenEndpoint);
                listenSocket.Listen(options.BackLog);

                IsRunning = true;

                _cancellationTokenSource = new CancellationTokenSource();

                KeepAccept(listenSocket);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to listen on {options.Ip}:{options.Port}.", e);
            }
        }

        private async void KeepAccept(Socket listenSocket)
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var client = await listenSocket.AcceptAsync();
                    OnNewClientAccept(client);
                }
                catch
                {
                    break;
                }
            }

            _stopTaskCompletionSource.TrySetResult(true);
        }

        public event NewClientAcceptHandler NewClientAccepted;

        private void OnNewClientAccept(Socket socket)
        {
            var handler = NewClientAccepted;

            if (handler == null)
                return;

            handler(this, _channelFactory(socket));
        }

        public Task StopAsync()
        {
            _stopTaskCompletionSource = new TaskCompletionSource<bool>();

            _cancellationTokenSource.Cancel();
            _listenSocket.Close();
            
            return _stopTaskCompletionSource.Task;
        }
    }
}