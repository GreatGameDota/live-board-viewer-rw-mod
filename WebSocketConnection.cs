using System.Net.WebSockets;
using System.Text;

namespace LiveBoardViewer;

public class WebSocketConnection : IDisposable
{
    public string ServerUrl = "wss://rw-bingo-board-viewer.onrender.com";
    private ClientWebSocket webSocket;
    private CancellationTokenSource operationCancellationTokenSource;
    private CancellationTokenSource receiveCancellationTokenSource;
    private bool isConnected = false;
    private int reconnectAttempts = 0;
    public bool IsConnected => isConnected && webSocket?.State == WebSocketState.Open;

    public async Task ConnectAsync()
    {
        try
        {
            if (IsConnected) return;

            webSocket?.Dispose();
            operationCancellationTokenSource?.Dispose();
            receiveCancellationTokenSource?.Dispose();

            webSocket = new ClientWebSocket();
            operationCancellationTokenSource = new CancellationTokenSource();
            receiveCancellationTokenSource = new CancellationTokenSource();

            using (var connectionTimeoutCts = new CancellationTokenSource(10000))
            {
                // LiveBoardViewer.logger.LogInfo($"Attempting to connect to {ServerUrl}...");
                await webSocket.ConnectAsync(new Uri(ServerUrl), connectionTimeoutCts.Token);
            }

            if (webSocket?.State == WebSocketState.Open)
            {
                isConnected = true;
                reconnectAttempts = 0;

                _ = ReceiveMessagesAsync(receiveCancellationTokenSource.Token);
                LiveBoardViewer.logger.LogInfo("WebSocket connected successfully");
            }
        }
        catch (Exception ex)
        {
            isConnected = false;
            reconnectAttempts++;
            LiveBoardViewer.logger.LogInfo($"WebSocket connection failed (attempt {reconnectAttempts}): {ex.Message}");
        }
    }

    public async Task SendAsync(string data)
    {
        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            var bytes = Encoding.UTF8.GetBytes(data);
            var buffer = new ArraySegment<byte>(bytes);

            await webSocket.SendAsync(
                buffer,
                WebSocketMessageType.Text,
                true,
                operationCancellationTokenSource.Token
            );

            // LiveBoardViewer.logger.LogInfo($"Data sent: {data.Length} bytes");
        }
        catch (Exception ex)
        {
            LiveBoardViewer.logger.LogInfo($"Failed to send data via WebSocket: {ex.Message}");
            if (isConnected)
            {
                await DisconnectAsync();
            }
            // throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            isConnected = false;
            receiveCancellationTokenSource?.Cancel();

            if (webSocket?.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            LiveBoardViewer.logger.LogInfo($"WebSocket connection closed.");
        }
        catch (Exception ex)
        {
            LiveBoardViewer.logger.LogInfo($"Error during disconnect: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            DisconnectAsync().Wait(1000);
            webSocket?.Dispose();
            operationCancellationTokenSource?.Dispose();
            receiveCancellationTokenSource?.Dispose();
        }
        catch
        {
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 4];
        try
        {
            while (IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken
                );

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await DisconnectAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LiveBoardViewer.logger.LogInfo($"Receive error: {ex.Message}");
            await DisconnectAsync();
        }
    }
}