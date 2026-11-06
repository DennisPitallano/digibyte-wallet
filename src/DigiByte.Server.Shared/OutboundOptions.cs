namespace DigiByte.Server.Shared;

/// <summary>Bound from the <c>Outbound</c> configuration section. <see cref="AllowLoopback"/> is for development only and defaults to false.</summary>
public sealed class OutboundOptions
{
    public const string SectionName = "Outbound";

    public bool AllowLoopback { get; set; }

    public OutboundUrlPolicy DigiId() => OutboundUrlPolicy.DigiId(AllowLoopback);

    public OutboundUrlPolicy Webhook() => OutboundUrlPolicy.Webhook(AllowLoopback);
}
