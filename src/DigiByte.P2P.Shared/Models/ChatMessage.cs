namespace DigiByte.P2P.Shared.Models;

public class ChatMessage
{
    public Guid Id { get; set; }
    public required Guid TradeId { get; set; }
    public required Guid SenderId { get; set; }
    public required string Content { get; set; }
    public ChatMessageType Type { get; set; } = ChatMessageType.Text;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}

public enum ChatMessageType
{
    Text,
    System,     // "Trade initiated", "Payment marked as sent", etc.
    Image       // Payment proof screenshot
}
