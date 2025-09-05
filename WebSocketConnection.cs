using System.Net.WebSockets;
using System.Text;

namespace LiveBoardViewer;

public class WebSocketConnection : IDisposable
{
    public string ServerUrl = "wss://rw-bingo-board-viewer.onrender.com";
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellationTokenSource;
    private bool isConnected = false;
    private int reconnectAttempts = 0;
    public bool IsConnected => isConnected && webSocket?.State == WebSocketState.Open;

    public async Task ConnectAsync()
    {
        try
        {
            if (IsConnected) return;

            webSocket?.Dispose();
            cancellationTokenSource?.Dispose();

            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource(10000);

            LiveBoardViewer.logger.LogInfo($"Attempting to connect to {ServerUrl}...");

            await webSocket.ConnectAsync(new Uri(ServerUrl), cancellationTokenSource.Token);

            isConnected = true;
            reconnectAttempts = 0;

            LiveBoardViewer.logger.LogInfo("WebSocket connected successfully");
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
                cancellationTokenSource.Token
            );

            LiveBoardViewer.logger.LogInfo($"Data sent: {data.Length} bytes");
        }
        catch (Exception ex)
        {
            LiveBoardViewer.logger.LogInfo($"Failed to send data via WebSocket: {ex.Message}");
            await DisconnectAsync();
            // throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            isConnected = false;

            if (webSocket?.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
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
            cancellationTokenSource?.Dispose();
        }
        catch
        {
        }
    }
}