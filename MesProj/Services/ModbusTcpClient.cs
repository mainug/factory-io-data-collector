using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MesProj.Services
{
    internal sealed class ModbusTcpClient : IDisposable
    {
        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId;
        private byte _unitId;

        public bool IsConnected { get { return _client != null && _client.Connected && _stream != null; } }

        public async Task ConnectAsync(string host, int port, byte unitId, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            DisposeConnection();
            var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);
            if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) != connectTask)
            {
                client.Close();
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Modbus TCP 연결 시간이 초과되었습니다.");
            }

            await connectTask.ConfigureAwait(false);
            client.NoDelay = true;
            client.ReceiveTimeout = timeoutMilliseconds;
            client.SendTimeout = timeoutMilliseconds;
            _client = client;
            _stream = client.GetStream();
            _unitId = unitId;
        }

        public async Task<bool> ReadDiscreteInputAsync(ushort address, CancellationToken cancellationToken)
        {
            var response = await SendAsync(2, AddressAndQuantity(address, 1), cancellationToken).ConfigureAwait(false);
            ValidateReadBitsResponse(response);
            return (response[2] & 1) != 0;
        }

        public async Task<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken)
        {
            var response = await SendAsync(3, AddressAndQuantity(address, 1), cancellationToken).ConfigureAwait(false);
            if (response.Length < 4 || response[1] != 2) throw new IOException("잘못된 Holding Register 응답입니다.");
            return (ushort)((response[2] << 8) | response[3]);
        }

        public async Task<ushort> ReadInputRegisterAsync(ushort address, CancellationToken cancellationToken)
        {
            var response = await SendAsync(4, AddressAndQuantity(address, 1), cancellationToken).ConfigureAwait(false);
            if (response.Length < 4 || response[1] != 2) throw new IOException("잘못된 Input Register 응답입니다.");
            return (ushort)((response[2] << 8) | response[3]);
        }

        public async Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken cancellationToken)
        {
            var data = new byte[]
            {
                (byte)(address >> 8), (byte)address,
                value ? (byte)0xFF : (byte)0x00, 0x00
            };
            var response = await SendAsync(5, data, cancellationToken).ConfigureAwait(false);
            if (response.Length != 5) throw new IOException("잘못된 Coil 쓰기 응답입니다.");
        }

        public void Dispose()
        {
            DisposeConnection();
            _requestLock.Dispose();
        }

        public void Disconnect()
        {
            DisposeConnection();
        }

        private async Task<byte[]> SendAsync(byte functionCode, byte[] data, CancellationToken cancellationToken)
        {
            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsConnected) throw new InvalidOperationException("Modbus TCP가 연결되어 있지 않습니다.");
                var transactionId = unchecked(++_transactionId);
                var length = 2 + data.Length;
                var request = new byte[8 + data.Length];
                request[0] = (byte)(transactionId >> 8);
                request[1] = (byte)transactionId;
                request[4] = (byte)(length >> 8);
                request[5] = (byte)length;
                request[6] = _unitId;
                request[7] = functionCode;
                Buffer.BlockCopy(data, 0, request, 8, data.Length);
                await _stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);

                var header = await ReadExactAsync(7, cancellationToken).ConfigureAwait(false);
                var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
                var responseLength = (header[4] << 8) | header[5];
                if (responseTransactionId != transactionId || header[2] != 0 || header[3] != 0 || responseLength < 2)
                    throw new IOException("잘못된 Modbus TCP 응답 헤더입니다.");

                var response = await ReadExactAsync(responseLength - 1, cancellationToken).ConfigureAwait(false);
                if ((response[0] & 0x80) != 0)
                    throw new IOException("Modbus 예외 응답: function=" + functionCode + ", code=" + response[1]);
                if (response[0] != functionCode) throw new IOException("Modbus Function Code가 일치하지 않습니다.");
                return response;
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = await _stream.ReadAsync(buffer, offset, count - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new IOException("Modbus TCP 연결이 종료되었습니다.");
                offset += read;
            }
            return buffer;
        }

        private static byte[] AddressAndQuantity(ushort address, ushort quantity)
        {
            return new[] { (byte)(address >> 8), (byte)address, (byte)(quantity >> 8), (byte)quantity };
        }

        private static void ValidateReadBitsResponse(byte[] response)
        {
            if (response.Length < 3 || response[1] < 1) throw new IOException("잘못된 Discrete Input 응답입니다.");
        }

        private void DisposeConnection()
        {
            if (_stream != null) _stream.Dispose();
            if (_client != null) _client.Close();
            _stream = null;
            _client = null;
        }
    }
}
